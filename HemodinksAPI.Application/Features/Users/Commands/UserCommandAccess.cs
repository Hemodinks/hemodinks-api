using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class UserCommandAccess
{
    public static bool CanUpdateUser(CurrentUserContext currentUser, int userId)
    {
        return currentUser.IsAdministrador
            || ((currentUser.IsMedico || currentUser.IsPaciente) && currentUser.Id == userId);
    }

    public static bool CanManageUserFiles(CurrentUserContext currentUser, int userId)
    {
        return currentUser.IsAdministrador || (currentUser.IsMedico && currentUser.Id == userId);
    }
}
