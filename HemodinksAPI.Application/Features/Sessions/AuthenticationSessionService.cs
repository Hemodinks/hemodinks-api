using System.Security.Cryptography;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Sessions;

public sealed class AuthenticationSessionOptions
{
    public const string SectionName = "AuthenticationSession";

    public int IdleTimeoutMinutes { get; set; } = 30;
    public int AbsoluteLifetimeHours { get; set; } = 12;
    // Opt-in: zero preserves the existing exact idle deadline.
    public int ActivityPersistenceIntervalSeconds { get; set; }

    public string RefreshCookieName { get; set; } = "hemodinks_refresh";

    public int RefreshCookieLifetimeDays { get; set; } = 30;
    public string RefreshCookieSameSite { get; set; } = "None";
}

public sealed record IssuedAuthenticationSession(
    string AccessToken,
    string RefreshToken,
    DateTime IdleExpiresAt,
    DateTime RefreshCookieExpiresAt)
{
    public HemodinksAPI.Application.Features.Users.Commands.AuthenticateUserResponse Identity { get; init; } = null!;
}

public sealed record AuthenticationSessionValidation(
    bool IsValid,
    int? PerfilId = null,
    string? PerfilNome = null,
    int? UsuarioClinicaId = null,
    string? FailureCode = null,
    DateTime? AuthenticatedAt = null,
    SessionValidationSnapshot? Snapshot = null);

public sealed class SessionRefreshConflictException : Exception;

public sealed partial class AuthenticationSessionService
{
    private readonly IAuthenticationSessionStore _store;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly AuthenticationSessionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SessionLifetimePolicy _lifetime;
    private readonly ILogger<AuthenticationSessionService> _logger;

    public AuthenticationSessionService(
        IAuthenticationSessionStore store,
        IJwtTokenService jwtTokenService,
        AuthenticationSessionOptions options,
        TimeProvider timeProvider,
        ILogger<AuthenticationSessionService> logger)
    {
        _store = store;
        _jwtTokenService = jwtTokenService;
        _options = options;
        _timeProvider = timeProvider;
        _lifetime = new SessionLifetimePolicy(options, timeProvider);
        _logger = logger;
    }

    public async Task<IssuedAuthenticationSession?> StartAsync(
        int usuarioGlobalId,
        int userId,
        int clinicaId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        Guid? expectedSecurityVersion = null, TeamSessionIdentity? team = null)
    {
        var membership = await _store.FindActiveMembershipAsync(
            usuarioGlobalId,
            userId,
            clinicaId,
            cancellationToken);

        if (membership == null || (expectedSecurityVersion.HasValue && membership.UsuarioGlobal.SecurityVersion != expectedSecurityVersion))
        {
            return null;
        }

        if ((membership.User.PerfilId == Perfil.EquipeId) != (team != null)) return null;
        var now = UtcNow();
        if (team != null && _lifetime.Failure(team.AuthenticatedAt) != null) return null;
        var refreshToken = GenerateRefreshToken();
        var session = new AuthenticationSession
        {
            Id = Guid.NewGuid(),
            SecurityVersion = membership.UsuarioGlobal.SecurityVersion,
            UsuarioGlobalId = membership.UsuarioGlobalId,
            UsuarioClinicaId = membership.Id,
            UsuarioClinica = membership,
            RefreshTokenHash = HashRefreshToken(refreshToken),
            CreatedAt = team?.AuthenticatedAt ?? now,
            EquipeId = team?.EquipeId,
            EquipeOperadorId = team?.OperadorId,
            EquipeVersaoSessao = team?.EquipeVersion,
            OperadorVersaoSessao = team?.OperadorVersion,
            IdentificacaoConfiavel = team?.Reliable ?? false,
            LastActivityAt = now,
            CreatedByIp = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 512)
        };

        if (!await HasValidTeamAsync(session, cancellationToken)) return null;
        _store.Add(session);
        await _store.SaveChangesAsync(cancellationToken);

        return await IssueAsync(session, membership, refreshToken, cancellationToken);
    }

    public async Task<IssuedAuthenticationSession?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken,
        Guid? expectedSessionId = null,
        int? expectedMembershipId = null,
        bool touchActivity = false)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var session = await _store.FindByRefreshTokenHashAsync(tokenHash, cancellationToken);

        var now = UtcNow();
        if (session != null && ((expectedSessionId.HasValue && session.Id != expectedSessionId)
            || (expectedMembershipId.HasValue && session.UsuarioClinicaId != expectedMembershipId)))
        {
            return null;
        }
        if (session == null)
        {
            _logger.LogWarning("Refresh token de sessao nao encontrado");
            return null;
        }

        var lifetimeFailure = _lifetime.Failure(session.CreatedAt, session.LastActivityAt);
        var sessionIsActive = IsActive(session);
        var membershipIsActive = IsActive(session.UsuarioClinica)
            && await HasValidTeamAsync(session, cancellationToken);
        if (!sessionIsActive || !membershipIsActive)
        {
            _logger.LogInformation(
                "Refresh recusado para sessao {SessionId}. SessaoAtiva: {SessionIsActive}; VinculoAtivo: {MembershipIsActive}; UltimaAtividade: {LastActivityAt}",
                session.Id,
                sessionIsActive,
                membershipIsActive,
                session.LastActivityAt);
            if (session.RevokedAt == null)
            {
                session.RevokedAt = now;
                await SaveRevocationAsync(cancellationToken);
            }

            if (lifetimeFailure == SessionLifetimePolicy.AbsoluteExpired) throw new SessionAbsoluteExpiredException();
            return null;
        }

        var newRefreshToken = GenerateRefreshToken();
        // A refresh timer alone is not user activity. Only foreground interactions extend idle time.
        if (touchActivity) session.LastActivityAt = Later(session.LastActivityAt, now);
        session.UsuarioClinica.PerfilId = session.UsuarioClinica.User.PerfilId;
        session.RefreshTokenHash = HashRefreshToken(newRefreshToken);

        if (!await _store.TrySaveChangesAsync(cancellationToken))
        {
            if (_lifetime.Failure(session.CreatedAt) == SessionLifetimePolicy.AbsoluteExpired)
            {
                await _store.RevokeByIdAsync(session.Id, UtcNow(), cancellationToken);
                throw new SessionAbsoluteExpiredException();
            }
            _logger.LogWarning("Tentativa concorrente de renovar a sessao {SessionId}", session.Id);
            throw new SessionRefreshConflictException();
        }

        if (_lifetime.Failure(session.CreatedAt) == SessionLifetimePolicy.AbsoluteExpired)
        {
            await _store.RevokeByIdAsync(session.Id, UtcNow(), cancellationToken);
            throw new SessionAbsoluteExpiredException();
        }
        return await IssueAsync(session, session.UsuarioClinica, newRefreshToken, cancellationToken);
    }

    public async Task<AuthenticationSessionValidation> ValidateAndTouchAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // Each attempt reads security state again. No state survives the request.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var session = await _store.FindByIdAsync(sessionId, cancellationToken);
            var now = UtcNow();

            if (session == null || !IsActive(session) || !IsActive(session.UsuarioClinica) || !await HasValidTeamAsync(session, cancellationToken))
            {
                if (session is { RevokedAt: null })
                {
                    session.RevokedAt = now;
                    await SaveRevocationAsync(cancellationToken);
                }

                return new AuthenticationSessionValidation(false, FailureCode: session == null ? null
                    : _lifetime.Failure(session.CreatedAt, session.LastActivityAt));
            }

            // O cadastro local e a fonte canonica do perfil. Uma promocao ou remocao de
            // permissao pode ocorrer enquanto ainda existe um access token valido; nesse
            // intervalo, o claim do JWT nao pode continuar autorizando com o perfil antigo.
            var currentProfileId = session.UsuarioClinica.User.PerfilId;
            if (session.UsuarioClinica.PerfilId != currentProfileId)
            {
                session.UsuarioClinica.PerfilId = currentProfileId;
                session.UsuarioClinica.DataAtualizacao = now;
            }

            var activityDue = now > session.LastActivityAt
                && now - session.LastActivityAt >= TimeSpan.FromSeconds(_options.ActivityPersistenceIntervalSeconds);
            if (activityDue) session.LastActivityAt = now;
            if (!await _store.TrySaveChangesAsync(cancellationToken))
            {
                // The competing operation may revoke or change context. Never authorize from
                // the stale aggregate or silently lose an activity update after a conflict.
                continue;
            }

            var failure = _lifetime.Failure(session.CreatedAt, session.LastActivityAt);
            if (failure != null) return new AuthenticationSessionValidation(false, FailureCode: failure);
            currentProfileId = session.UsuarioClinica.User.PerfilId;

            return new AuthenticationSessionValidation(
                true,
                currentProfileId,
                session.UsuarioClinica.User.Perfil.Nome,
                session.UsuarioClinicaId,
                AuthenticatedAt: session.CreatedAt,
                Snapshot: SessionValidationSnapshot.From(session));
        }
        return new AuthenticationSessionValidation(false);
    }

    public async Task<bool> ChangeMembershipAsync(
        Guid sessionId,
        int usuarioClinicaId,
        CancellationToken cancellationToken)
    {
        var session = await _store.FindByIdAsync(sessionId, cancellationToken);
        if (session == null || !IsActive(session) || !IsActive(session.UsuarioClinica) || !await HasValidTeamAsync(session, cancellationToken))
        {
            return false;
        }

        // A context transition can only select another active membership of this identity.
        var membership = await _store.FindMembershipAsync(session.UsuarioGlobalId, usuarioClinicaId, cancellationToken);
        if (membership == null || session.EquipeId.HasValue || membership.User.PerfilId == Perfil.EquipeId) return false;
        session.UsuarioClinicaId = usuarioClinicaId;
        session.LastActivityAt = Later(session.LastActivityAt, UtcNow());
        return await _store.TrySaveChangesAsync(cancellationToken)
            && _lifetime.Failure(session.CreatedAt) == null;
    }

    public async Task RevokeAsync(string? refreshToken, Guid? sessionId, CancellationToken cancellationToken)
    {
        AuthenticationSession? session = null;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var tokenHash = HashRefreshToken(refreshToken);
            session = await _store.FindByRefreshTokenHashAsync(tokenHash, cancellationToken);
        }

        if (session == null && sessionId.HasValue)
        {
            session = await _store.FindByIdAsync(sessionId.Value, cancellationToken);
        }

        if (session is { RevokedAt: null })
        {
            session.RevokedAt = UtcNow();
            await _store.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> RevokeMatchingAsync(string refreshToken, Guid sessionId, int membershipId,
        CancellationToken cancellationToken, Guid? authenticatedSessionId = null, int? authenticatedMembershipId = null)
    {
        var cookieSession = await _store.FindByRefreshTokenHashAsync(HashRefreshToken(refreshToken), cancellationToken);
        var cookieMatches = cookieSession?.Id == sessionId && cookieSession.UsuarioClinicaId == membershipId;
        if (cookieMatches)
            return await _store.RevokeByIdAsync(sessionId, UtcNow(), cancellationToken);
        if (authenticatedSessionId == sessionId && authenticatedMembershipId == membershipId)
            await _store.RevokeByIdAsync(sessionId, UtcNow(), cancellationToken);
        // An old bearer may revoke its own session, but must never delete a different
        // session's cookie (or an unrecognized cookie rotated by another request).
        return false;
    }

    private static bool IsActive(UsuarioClinica membership)
    {
        return membership.Ativo
            && membership.UsuarioGlobal.Ativo
            && membership.User.Ativo
            && membership.User.ClinicaId == membership.ClinicaId
            && membership.Clinica.Ativa;
    }

    private bool IsActive(AuthenticationSession session)
    {
        return session.UsuarioGlobalId == session.UsuarioClinica.UsuarioGlobalId
            && session.SecurityVersion == session.UsuarioClinica.UsuarioGlobal.SecurityVersion
            && session.RevokedAt == null
            && _lifetime.Failure(session.CreatedAt, session.LastActivityAt) == null;
    }

    private async Task SaveRevocationAsync(CancellationToken cancellationToken)
    {
        // A sessao pode ter sido atualizada ou revogada por outra requisicao.
        _ = await _store.TrySaveChangesAsync(cancellationToken);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static DateTime Later(DateTime left, DateTime right) => left >= right ? left : right;

    private static string GenerateRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(64));

    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
    }
}
