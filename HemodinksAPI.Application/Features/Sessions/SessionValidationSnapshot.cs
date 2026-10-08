using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Sessions;

// Immutable values only: never share EF entities or use this across requests.
public sealed record SessionValidationSnapshot(
    Guid SessionId, int UsuarioGlobalId, int UsuarioClinicaId, int UserId,
    int ClinicaId, string ClinicaNome, string ClinicaSlug, Guid SecurityVersion,
    bool TemporaryPasswordRecovery, int? EquipeId, int? EquipeVersion,
    int? OperadorId, int? OperadorVersion, bool Reliable, int PerfilId)
{
    internal static SessionValidationSnapshot From(AuthenticationSession session)
    {
        var member = session.UsuarioClinica;
        return new(session.Id, member.UsuarioGlobalId, member.Id, member.UserId,
            member.ClinicaId, member.Clinica.Nome, member.Clinica.Slug,
            member.UsuarioGlobal.SecurityVersion, member.UsuarioGlobal.TemporaryPasswordRecovery,
            session.EquipeId, session.EquipeVersaoSessao, session.EquipeOperadorId,
            session.OperadorVersaoSessao, session.IdentificacaoConfiavel, member.User.PerfilId);
    }
}
