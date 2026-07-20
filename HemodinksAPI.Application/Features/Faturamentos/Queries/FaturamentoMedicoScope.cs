using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

internal static class FaturamentoMedicoScope
{
    public static IQueryable<Paciente> ApplyScope(IQueryable<Paciente> query, int perfilId, int currentUserId)
    {
        if (Perfil.IsAdministradorOuSuper(perfilId) || perfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            return query.Where(p => p.MedicoUserId == currentUserId);
        }

        return query.Where(_ => false);
    }
}
