using System.Security.Claims;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Application.Authorization;

public sealed record CurrentUserContext(
    int Id,
    int PerfilId,
    string Nome,
    int ClinicaId = Clinica.DefaultId,
    string ClinicaSlug = Clinica.DefaultSlug)
{
    public bool IsAdministrador => Perfil.IsAdministradorOuSuper(PerfilId);

    public bool IsSuperAdministrador => PerfilId == Perfil.SuperAdministradorId;

    public bool IsMedico => PerfilId == Perfil.MedicosId;

    public bool IsPaciente => PerfilId == Perfil.PacientesId;

    public bool IsController => PerfilId == Perfil.ControllerId;
}

public static class CurrentUserContextExtensions
{
    public static CurrentUserContext? ToCurrentUserContext(this ClaimsPrincipal claimsPrincipal)
    {
        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var perfilIdClaim = claimsPrincipal.FindFirst("perfilId")?.Value;
        var nome = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var clinicaIdClaim = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaId)?.Value;
        var clinicaSlug = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaSlug)?.Value ?? Clinica.DefaultSlug;

        if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(perfilIdClaim, out var perfilId))
        {
            return null;
        }

        var clinicaId = int.TryParse(clinicaIdClaim, out var parsedClinicaId) && parsedClinicaId > 0
            ? parsedClinicaId
            : Clinica.DefaultId;

        return new CurrentUserContext(userId, perfilId, nome, clinicaId, clinicaSlug);
    }
}
