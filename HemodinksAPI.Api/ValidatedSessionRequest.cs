using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

internal static class ValidatedSessionRequest
{
    private static readonly object Key = new();

    public static void Set(HttpContext context, SessionValidationSnapshot snapshot) => context.Items[Key] = snapshot;

    public static SessionValidationSnapshot? Get(HttpContext context)
    {
        if (context.Items[Key] is not SessionValidationSnapshot value || context.User.Identity?.IsAuthenticated != true)
            return null;
        var user = context.User;
        bool Matches(string claim, int? expected) => expected.HasValue
            ? int.TryParse(user.FindFirstValue(claim), out var actual) && actual == expected
            : user.FindFirst(claim) == null;
        return Guid.TryParse(user.FindFirstValue(AuthenticationSessionClaimTypes.SessionId), out var id) && id == value.SessionId
            && Guid.TryParse(user.FindFirstValue("security_version"), out var version) && version == value.SecurityVersion
            && Matches(ClaimTypes.NameIdentifier, value.UserId)
            && Matches(GlobalIdentityClaimTypes.UsuarioGlobalId, value.UsuarioGlobalId)
            && Matches(GlobalIdentityClaimTypes.UsuarioClinicaId, value.UsuarioClinicaId)
            && Matches(ClinicaClaimTypes.ClinicaId, value.ClinicaId)
            && Matches("perfilId", value.PerfilId)
            && Matches(GlobalIdentityClaimTypes.EquipeId, value.EquipeId)
            && Matches(GlobalIdentityClaimTypes.EquipeVersaoSessao, value.EquipeVersion)
            && Matches(GlobalIdentityClaimTypes.EquipeOperadorId, value.OperadorId)
            && Matches(GlobalIdentityClaimTypes.OperadorVersaoSessao, value.OperadorVersion)
            && (user.FindFirstValue(GlobalIdentityClaimTypes.IdentificacaoConfiavel) == "true") == value.Reliable
            ? value : null;
    }
}
