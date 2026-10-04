using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Sessions;

public sealed record TeamSessionIdentity(int EquipeId, int? OperadorId, int EquipeVersion,
    int? OperadorVersion, bool Reliable, DateTime AuthenticatedAt);
public sealed record SessionTeamBinding(Equipe Team, EquipeOperador? Operator);

public sealed partial class AuthenticationSessionService
{
    private async Task<bool> HasValidTeamAsync(AuthenticationSession session, CancellationToken ct) =>
        session.EquipeId.HasValue
            ? await _store.FindTeamBindingAsync(session, UtcNow(), ct) != null
            : session.UsuarioClinica.User.PerfilId != Perfil.EquipeId;

    private async Task<IssuedAuthenticationSession?> IssueAsync(AuthenticationSession session,
        UsuarioClinica membership, string refreshToken, CancellationToken ct)
    {
        var team = session.EquipeId.HasValue ? await _store.FindTeamBindingAsync(session, UtcNow(), ct) : null;
        if (session.EquipeId.HasValue && team == null) return null;
        var user = membership.User;
        var token = team == null
            ? _jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, user, session.Id, session.CreatedAt)
            : _jwtTokenService.GenerateToken(membership.UsuarioGlobal, membership, user, team.Team, team.Operator,
                session.IdentificacaoConfiavel, session.CreatedAt, session.Id);
        return new IssuedAuthenticationSession(token, refreshToken,
            session.LastActivityAt.AddMinutes(_options.IdleTimeoutMinutes), _lifetime.CookieExpiresAt(session.CreatedAt))
        {
            Identity = new AuthenticateUserResponse
            {
                Token = token, Id = user.Id, UsuarioGlobalId = membership.UsuarioGlobalId,
                ClinicaId = membership.ClinicaId, ClinicaSlug = user.Clinica.Slug,
                Nome = team?.Operator?.User.Nome ?? user.Nome, Email = membership.UsuarioGlobal.Email,
                PerfilId = user.PerfilId, PerfilNome = user.Perfil.Nome,
                Cpf = user.Cpf, Crm = user.Crm, CrmUf = user.CrmUf, FotoPerfil = user.FotoPerfil,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha || membership.UsuarioGlobal.TemporaryPasswordRecovery,
                PrecisaTrocarPin = session.IdentificacaoConfiavel && team?.Operator?.PrecisaTrocarPin == true,
                ModulosLiberados = ClinicaModulos.GetEffective(user.Clinica.Plano, user.Clinica.ModulosLiberados)
            }
        };
    }

    // Called only after the authenticated PIN use case succeeds, with its server-generated version.
    public async Task<bool> AdvanceOperatorVersionAsync(Guid id, int membershipId, int operatorId,
        int previousVersion, int nextVersion, CancellationToken ct)
    {
        var session = await _store.FindByIdAsync(id, ct);
        if (session == null || !IsActive(session) || !IsActive(session.UsuarioClinica)
            || session.UsuarioClinicaId != membershipId || session.EquipeOperadorId != operatorId
            || session.OperadorVersaoSessao != previousVersion || nextVersion != previousVersion + 1) return false;
        session.OperadorVersaoSessao = nextVersion;
        return await HasValidTeamAsync(session, ct) && await _store.TrySaveChangesAsync(ct);
    }
}
