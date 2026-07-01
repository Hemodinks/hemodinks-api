using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

internal static class PacienteAccess
{
    public static IQueryable<Paciente> ApplyScope(
        IAppDbContext context,
        IQueryable<Paciente> query,
        int perfilId,
        int userId)
    {
        if (perfilId == Perfil.AdministradorId || perfilId == Perfil.ControllerId)
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

        if (perfilId == Perfil.PacientesId)
        {
            return query.Where(p => p.UserId == userId);
        }

        return query.Where(_ => false);
    }
}
