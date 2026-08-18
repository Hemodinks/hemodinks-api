using System.Security.Claims;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Infrastructure.Authorization;

public static class CurrentUserClaimsExtensions
{
    public static CurrentUserContext? ToCurrentUserContext(this ClaimsPrincipal claimsPrincipal)
    {
        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var perfilIdClaim = claimsPrincipal.FindFirst("perfilId")?.Value;
        var nome = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var clinicaIdClaim = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaId)?.Value;
        var clinicaSlug = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaSlug)?.Value ?? Clinica.DefaultSlug;
        var usuarioGlobalIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioGlobalId)?.Value;
        var usuarioClinicaIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioClinicaId)?.Value;
        var equipeIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeId)?.Value;
        var equipeOperadorIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeOperadorId)?.Value;
        var identificacaoConfiavelClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.IdentificacaoConfiavel)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(perfilIdClaim, out var perfilId))
        {
            return null;
        }

        var clinicaId = int.TryParse(clinicaIdClaim, out var parsedClinicaId) && parsedClinicaId > 0
            ? parsedClinicaId
            : Clinica.DefaultId;

        _ = int.TryParse(usuarioGlobalIdClaim, out var usuarioGlobalId);
        _ = int.TryParse(usuarioClinicaIdClaim, out var usuarioClinicaId);
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
