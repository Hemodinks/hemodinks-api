using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordCommandAccess
{
    public static void EnsureCanChangeOwnPassword(CurrentUserContext? currentUser, int userId)
    {
        if (currentUser != null && currentUser.Id != userId)
        {
            throw new UnauthorizedAccessException("Sem permissao para alterar senha do usuario");
        }
    }
}
