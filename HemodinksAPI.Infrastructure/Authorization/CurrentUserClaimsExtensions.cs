using System.Security.Claims;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Infrastructure.Authorization;

public static class CurrentUserClaimsExtensions
{
    public static CurrentUserContext? ToCurrentUserContext(this ClaimsPrincipal claimsPrincipal)
    {
        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var perfilIdClaim = claimsPrincipal.FindFirst("perfilId")?.Value;
        var nome = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var clinicaIdClaim = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaId)?.Value;
        var clinicaSlug = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaSlug)?.Value;
        var usuarioGlobalIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioGlobalId)?.Value;
        var usuarioClinicaIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioClinicaId)?.Value;
        var equipeIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeId)?.Value;
        var equipeOperadorIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeOperadorId)?.Value;
        var identificacaoConfiavelClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.IdentificacaoConfiavel)?.Value;

        if (!int.TryParse(userIdClaim, out var userId)
            || userId <= 0
            || !int.TryParse(perfilIdClaim, out var perfilId)
            || perfilId <= 0
            || !int.TryParse(clinicaIdClaim, out var clinicaId)
            || clinicaId <= 0
            || string.IsNullOrWhiteSpace(clinicaSlug)
            || !int.TryParse(usuarioGlobalIdClaim, out var usuarioGlobalId)
            || usuarioGlobalId <= 0
            || !int.TryParse(usuarioClinicaIdClaim, out var usuarioClinicaId)
            || usuarioClinicaId <= 0)
        {
            return null;
        }
        var equipeId = int.TryParse(equipeIdClaim, out var parsedEquipeId) && parsedEquipeId > 0 ? parsedEquipeId : (int?)null;
        var equipeOperadorId = int.TryParse(equipeOperadorIdClaim, out var parsedOperadorId) && parsedOperadorId > 0 ? parsedOperadorId : (int?)null;
        var identificacaoConfiavel = bool.TryParse(identificacaoConfiavelClaim, out var parsedConfiavel) && parsedConfiavel;

        return new CurrentUserContext(
            userId,
            perfilId,
            nome,
            clinicaId,
            clinicaSlug,
            usuarioGlobalId,
            usuarioClinicaId,
            equipeId,
            equipeOperadorId,
            identificacaoConfiavel);
    }
}
