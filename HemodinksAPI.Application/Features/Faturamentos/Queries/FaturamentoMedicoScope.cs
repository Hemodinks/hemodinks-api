using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Data;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

internal static class FaturamentoMedicoScope
{
    public static IQueryable<Paciente> ApplyScope(
        IAppDbContext context,
        IQueryable<Paciente> query,
        int perfilId,
        int currentUserId,
        int? equipeId = null)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId) || perfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            return query.Where(p => p.MedicoUserId == currentUserId);
        }

        if (perfilId == Perfil.EquipeId && equipeId.HasValue)
        {
            var memberUserIds = context.EquipeMembros
                .Where(item => item.EquipeId == equipeId.Value && item.Ativo)
                .Select(item => item.UserId);
            return query.Where(paciente =>
                (paciente.MedicoUserId.HasValue && memberUserIds.Contains(paciente.MedicoUserId.Value))
                || (paciente.MedicoAuxiliar1UserId.HasValue && memberUserIds.Contains(paciente.MedicoAuxiliar1UserId.Value))
                || (paciente.MedicoAuxiliar2UserId.HasValue && memberUserIds.Contains(paciente.MedicoAuxiliar2UserId.Value)));
        }

        return query.Where(_ => false);
    }
}
