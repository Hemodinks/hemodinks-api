using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos;

internal static class MedicalGroupScope
{
    public static IQueryable<User> BuildScopedMedicalUsersQuery(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        bool onlyActive = true)
    {
        var query = context.Users
            .AsNoTracking()
            .Where(user => user.PerfilId == Perfil.MedicosId);

        if (onlyActive)
        {
            query = query.Where(user => user.Ativo);
        }

        if (Perfil.IsAdministradorOuSuper(currentPerfilId) || currentPerfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (currentPerfilId != Perfil.MedicosId)
        {
            return query.Where(_ => false);
        }

        var groupIds = context.GrupoMedicoUsuarios
            .AsNoTracking()
            .Where(member => member.UserId == currentUserId)
            .Select(member => member.GrupoMedicoId);

        return query.Where(user =>
            user.Id == currentUserId
            || user.GruposMedicos.Any(member => groupIds.Contains(member.GrupoMedicoId)));
    }

    public static IQueryable<int> BuildScopedMedicalUserIdsQuery(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId)
    {
        return BuildScopedMedicalUsersQuery(context, currentPerfilId, currentUserId, onlyActive: false)
            .Select(user => user.Id);
    }

    public static async Task<HashSet<int>> GetScopedMedicalUserIdsAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        return await BuildScopedMedicalUserIdsQuery(context, currentPerfilId, currentUserId)
            .ToHashSetAsync(cancellationToken);
    }
}
