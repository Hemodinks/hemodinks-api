using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static class PacienteCommandAccess
{
    public static bool CanCreate(int perfilId)
    {
        return perfilId == Perfil.AdministradorId
            || perfilId == Perfil.ControllerId
            || perfilId == Perfil.MedicosId;
    }

    public static bool CanManage(int perfilId)
    {
        return perfilId == Perfil.AdministradorId;
    }

    public static async Task<bool> CanEditPacienteAsync(IAppDbContext context, Paciente paciente, int perfilId, int currentUserId, CancellationToken cancellationToken)
    {
        if (perfilId == Perfil.AdministradorId || perfilId == Perfil.ControllerId)
        {
            return true;
        }

        if (perfilId == Perfil.MedicosId)
        {
            var accessibleMedicalUserIds = await MedicalGroupScope.GetScopedMedicalUserIdsAsync(context, perfilId, currentUserId, cancellationToken);
            return (paciente.MedicoUserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoUserId.Value))
                || (paciente.MedicoAuxiliar1UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar1UserId.Value))
                || (paciente.MedicoAuxiliar2UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar2UserId.Value));
        }

        return false;
    }

    public static Task<bool> CanManagePacienteArquivoAsync(IAppDbContext context, Paciente paciente, int perfilId, int currentUserId, CancellationToken cancellationToken)
    {
        return CanEditPacienteAsync(context, paciente, perfilId, currentUserId, cancellationToken);
    }
}
