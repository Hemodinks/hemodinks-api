using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

internal static class UserQueryAccess
{
    public static void EnsureCanAccessUser(CurrentUserContext? currentUser, int requestedUserId)
    {
        if (currentUser != null
            && !currentUser.IsAdministrador
            && currentUser.Id != requestedUserId)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar usuario");
        }
    }

    public static async Task EnsureCanAccessProfilePhotoAsync(
        IAppDbContext context,
        CurrentUserContext currentUser,
        int requestedUserId,
        CancellationToken cancellationToken)
    {
        if (currentUser.IsAdministrador || currentUser.Id == requestedUserId)
        {
            return;
        }

        if (!currentUser.IsMedico)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar foto de perfil");
        }

        var accessibleMedicalUserIds = MedicalGroupScope.BuildScopedMedicalUserIdsQuery(
            context,
            currentUser.PerfilId,
            currentUser.Id);

        var canAccessPatientPhoto = await context.Pacientes
            .AsNoTracking()
            .Where(paciente =>
                (paciente.MedicoUserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoUserId.Value))
                || (paciente.MedicoAuxiliar1UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar1UserId.Value))
                || (paciente.MedicoAuxiliar2UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar2UserId.Value)))
            .AnyAsync(paciente => paciente.UserId == requestedUserId, cancellationToken);

        if (!canAccessPatientPhoto)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar foto de perfil");
        }
    }
}
