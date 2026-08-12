using System.Security.Cryptography;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class AuthenticationSessionOptions
{
    public const string SectionName = "AuthenticationSession";

    public int IdleTimeoutMinutes { get; set; } = 30;

    public string RefreshCookieName { get; set; } = "hemodinks_refresh";

    public int RefreshCookieLifetimeDays { get; set; } = 30;
}

public sealed record IssuedAuthenticationSession(
    string AccessToken,
    string RefreshToken,
    DateTime IdleExpiresAt,
    DateTime RefreshCookieExpiresAt);

public sealed class AuthenticationSessionService
{
    private readonly AppDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly AuthenticationSessionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthenticationSessionService> _logger;

    public AuthenticationSessionService(
        AppDbContext context,
        IJwtTokenService jwtTokenService,
        AuthenticationSessionOptions options,
        TimeProvider timeProvider,
        ILogger<AuthenticationSessionService> logger)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IssuedAuthenticationSession?> StartAsync(
        int usuarioGlobalId,
        int userId,
        int clinicaId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var membership = await ActiveMemberships()
            .FirstOrDefaultAsync(item => item.UsuarioGlobalId == usuarioGlobalId
                && item.UserId == userId
                && item.ClinicaId == clinicaId,
                cancellationToken);

        if (membership == null)
        {
            return null;
        }

        var now = UtcNow();
        var refreshToken = GenerateRefreshToken();
        var session = new AuthenticationSession
        {
            Id = Guid.NewGuid(),
            UsuarioGlobalId = membership.UsuarioGlobalId,
            UsuarioClinicaId = membership.Id,
            RefreshTokenHash = HashRefreshToken(refreshToken),
            CreatedAt = now,
            LastActivityAt = now,
            CreatedByIp = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 512)
        };

        _context.AuthenticationSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return Issue(session, membership, refreshToken);
    }

    public async Task<IssuedAuthenticationSession?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var session = await _context.AuthenticationSessions
            .IgnoreQueryFilters()
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.UsuarioGlobal)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.Clinica)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.UsuarioClinica).ThenInclude(item => item.User).ThenInclude(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.RefreshTokenHash == tokenHash, cancellationToken);

        var now = UtcNow();
        if (session == null)
        {
            _logger.LogWarning("Refresh token de sessao nao encontrado");
            return null;
        }

        var sessionIsActive = IsActive(session, now);
        var membershipIsActive = IsActive(session.UsuarioClinica);
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

            return null;
        }

        var newRefreshToken = GenerateRefreshToken();
        session.RefreshTokenHash = HashRefreshToken(newRefreshToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(exception, "Tentativa concorrente de renovar a sessao {SessionId}", session.Id);
            return null;
        }

        return Issue(session, session.UsuarioClinica, newRefreshToken);
    }

    public async Task<bool> ValidateAndTouchAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _context.AuthenticationSessions
            .FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        var now = UtcNow();

        if (session == null || !IsActive(session, now))
        {
            if (session is { RevokedAt: null })
            {
                session.RevokedAt = now;
                await SaveRevocationAsync(cancellationToken);
            }

            return false;
        }

        session.LastActivityAt = now;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Outra requisicao da mesma sessao atualizou a atividade primeiro.
            _logger.LogDebug(exception, "Atividade concorrente na sessao {SessionId}", sessionId);
        }

        return true;
    }

    public async Task<bool> ChangeMembershipAsync(
        Guid sessionId,
        int usuarioClinicaId,
        CancellationToken cancellationToken)
    {
        var session = await _context.AuthenticationSessions
            .FirstOrDefaultAsync(item => item.Id == sessionId && item.RevokedAt == null, cancellationToken);
        if (session == null)
        {
            return false;
        }

        session.UsuarioClinicaId = usuarioClinicaId;
        session.LastActivityAt = UtcNow();
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAsync(string? refreshToken, Guid? sessionId, CancellationToken cancellationToken)
    {
        AuthenticationSession? session = null;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var tokenHash = HashRefreshToken(refreshToken);
            session = await _context.AuthenticationSessions
                .FirstOrDefaultAsync(item => item.RefreshTokenHash == tokenHash, cancellationToken);
        }

        if (session == null && sessionId.HasValue)
        {
            session = await _context.AuthenticationSessions
                .FirstOrDefaultAsync(item => item.Id == sessionId.Value, cancellationToken);
        }

        if (session is { RevokedAt: null })
        {
            session.RevokedAt = UtcNow();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private IQueryable<UsuarioClinica> ActiveMemberships()
    {
        return _context.UsuariosClinicas
            .IgnoreQueryFilters()
            .Include(item => item.UsuarioGlobal)
            .Include(item => item.Clinica)
            .Include(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Perfil)
            .Include(item => item.User).ThenInclude(item => item.Clinica)
            .Where(IsActiveExpression());
    }

    private static System.Linq.Expressions.Expression<Func<UsuarioClinica, bool>> IsActiveExpression()
    {
        return item => item.Ativo
            && item.UsuarioGlobal.Ativo
            && item.User.Ativo
            && item.Clinica.Ativa;
    }

    private static bool IsActive(UsuarioClinica membership)
    {
        return membership.Ativo
            && membership.UsuarioGlobal.Ativo
            && membership.User.Ativo
            && membership.Clinica.Ativa;
    }

    private bool IsActive(AuthenticationSession session, DateTime now)
    {
        return session.RevokedAt == null
            && session.LastActivityAt > now.AddMinutes(-_options.IdleTimeoutMinutes);
    }

    private IssuedAuthenticationSession Issue(
        AuthenticationSession session,
        UsuarioClinica membership,
        string refreshToken)
    {
        return new IssuedAuthenticationSession(
            _jwtTokenService.GenerateToken(
                membership.UsuarioGlobal,
                membership,
                membership.User,
                session.Id),
            refreshToken,
            session.LastActivityAt.AddMinutes(_options.IdleTimeoutMinutes),
            UtcNow().AddDays(_options.RefreshCookieLifetimeDays));
    }

    private async Task SaveRevocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A sessao ja foi atualizada ou revogada por outra requisicao.
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string GenerateRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(64));

    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string? Truncate(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
    }
}
