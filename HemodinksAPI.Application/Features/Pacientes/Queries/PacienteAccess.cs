using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteAccess
{
    public static IQueryable<Paciente> ApplyScope(
        IMedicalUserScopeDbContext context,
        IQueryable<Paciente> query,
        int perfilId,
        int userId,
        int? equipeId = null)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId) || perfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            var accessibleMedicalUserIds = MedicalGroupScope.BuildScopedMedicalUserIdsQuery(context, perfilId, userId);
            return query.Where(p =>
                (p.MedicoUserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoUserId.Value))
                || (p.MedicoAuxiliar1UserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoAuxiliar1UserId.Value))
                || (p.MedicoAuxiliar2UserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoAuxiliar2UserId.Value)));
        }

        if (perfilId == Perfil.EquipeId && equipeId.HasValue)
        {
            var memberUserIds = context.EquipeMembros
                .Where(item => item.EquipeId == equipeId.Value && item.Ativo)
                .Select(item => item.UserId);
            return query.Where(p =>
                (p.MedicoUserId.HasValue && memberUserIds.Contains(p.MedicoUserId.Value))
                || (p.MedicoAuxiliar1UserId.HasValue && memberUserIds.Contains(p.MedicoAuxiliar1UserId.Value))
                || (p.MedicoAuxiliar2UserId.HasValue && memberUserIds.Contains(p.MedicoAuxiliar2UserId.Value)));
        }

        if (perfilId == Perfil.PacientesId)
        {
            return query.Where(p => p.UserId == userId);
        }

        return query.Where(_ => false);
    }
}
