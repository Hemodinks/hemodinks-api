using System.Security.Claims;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Authorization;

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
        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var perfilIdClaim = claimsPrincipal.FindFirst("perfilId")?.Value;
        var nome = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

        if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(perfilIdClaim, out var perfilId))
        {
            return null;
        }

        return new CurrentUserContext(userId, perfilId, nome);
    }
}
