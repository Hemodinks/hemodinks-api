using System.Security.Claims;
using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Api;

public static partial class GrupoMedicoEndpointExtensions
{
    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
