using System.Security.Claims;
using HemodinksAPI.Api.Models;

namespace HemodinksAPI.Api.Authorization;

public sealed record CurrentUserContext(int Id, int PerfilId, string Nome)
{
    public bool IsAdministrador => PerfilId == Perfil.AdministradorId;

    public bool IsMedico => PerfilId == Perfil.MedicosId;

    public bool IsPaciente => PerfilId == Perfil.PacientesId;
}

public static class CurrentUserContextExtensions
{
    public static CurrentUserContext? ToCurrentUserContext(this ClaimsPrincipal claimsPrincipal)
    {
        var userIdClaim = claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        var perfilIdClaim = claimsPrincipal.FindFirstValue("perfilId");
        var nome = claimsPrincipal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

        if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(perfilIdClaim, out var perfilId))
        {
            return null;
        }

        return new CurrentUserContext(userId, perfilId, nome);
    }
}
