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
        int? currentEquipeId = null,
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

        if (currentPerfilId == Perfil.EquipeId && currentEquipeId.HasValue)
        {
            var memberUserIds = context.EquipeMembros
                .AsNoTracking()
                .Where(member => member.EquipeId == currentEquipeId.Value && member.Ativo)
                .Select(member => member.UserId);
            return query.Where(user => memberUserIds.Contains(user.Id));
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
        int currentUserId,
        int? currentEquipeId = null)
    {
        return BuildScopedMedicalUsersQuery(context, currentPerfilId, currentUserId, currentEquipeId, onlyActive: false)
            .Select(user => user.Id);
    }

    public static async Task<HashSet<int>> GetScopedMedicalUserIdsAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        int? currentEquipeId,
        CancellationToken cancellationToken)
    {
        return await BuildScopedMedicalUserIdsQuery(context, currentPerfilId, currentUserId, currentEquipeId)
            .ToHashSetAsync(cancellationToken);
    }
}
