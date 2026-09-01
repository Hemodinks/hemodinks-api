using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static class PacienteCommandAccess
{
    public static bool CanCreate(int perfilId)
    {
        return Perfil.IsAdministradorOuSuper(perfilId)
            || perfilId == Perfil.ControllerId
            || perfilId == Perfil.MedicosId
            || perfilId == Perfil.EquipeId;
    }

    public static bool CanManage(int perfilId)
    {
        return Perfil.IsAdministradorOuSuper(perfilId);
    }

    public static async Task<bool> CanEditPacienteAsync(IPatientFeatureDbContext context, Paciente paciente, int perfilId, int currentUserId, int? equipeId, CancellationToken cancellationToken)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId) || perfilId == Perfil.ControllerId)
        {
            return true;
        }

        if (perfilId == Perfil.MedicosId)
        {
            var accessibleMedicalUserIds = await MedicalGroupScope.GetScopedMedicalUserIdsAsync(context, perfilId, currentUserId, null, cancellationToken);
            return (paciente.MedicoUserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoUserId.Value))
                || (paciente.MedicoAuxiliar1UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar1UserId.Value))
                || (paciente.MedicoAuxiliar2UserId.HasValue && accessibleMedicalUserIds.Contains(paciente.MedicoAuxiliar2UserId.Value));
        }

        if (perfilId == Perfil.EquipeId && equipeId.HasValue)
        {
            var memberUserIds = await MedicalGroupScope.GetScopedMedicalUserIdsAsync(
                context, perfilId, currentUserId, equipeId, cancellationToken);
            return (paciente.MedicoUserId.HasValue && memberUserIds.Contains(paciente.MedicoUserId.Value))
                || (paciente.MedicoAuxiliar1UserId.HasValue && memberUserIds.Contains(paciente.MedicoAuxiliar1UserId.Value))
                || (paciente.MedicoAuxiliar2UserId.HasValue && memberUserIds.Contains(paciente.MedicoAuxiliar2UserId.Value));
        }

        return false;
    }

    public static Task<bool> CanManagePacienteArquivoAsync(IPatientFeatureDbContext context, Paciente paciente, int perfilId, int currentUserId, int? equipeId, CancellationToken cancellationToken)
    {
        return CanEditPacienteAsync(context, paciente, perfilId, currentUserId, equipeId, cancellationToken);
    }
}
