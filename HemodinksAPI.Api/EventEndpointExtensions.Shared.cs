using System.Security.Claims;
using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Api;

public static partial class EventEndpointExtensions
{
    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }

    private static CurrentUserContext GetRequiredNonPatientCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        var currentUser = GetRequiredCurrentUser(claimsPrincipal);
        if (currentUser.IsPaciente)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar este recurso");
        }

        return currentUser;
    }
}
