using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

internal static class GrupoMedicoRules
{
    public static async Task ValidateAsync(
        IAppDbContext context,
        int? currentGroupId,
        string nome,
        IReadOnlyCollection<int> memberIds,
        CancellationToken cancellationToken)
    {
        var duplicateName = await context.GruposMedicos
            .AsNoTracking()
            .AnyAsync(group => group.Nome == nome && (!currentGroupId.HasValue || group.Id != currentGroupId.Value), cancellationToken);

        if (duplicateName)
        {
            throw new InvalidOperationException("Ja existe um grupo medico com esse nome.");
        }

        var validMedicalUsers = await context.Users
            .AsNoTracking()
            .Where(user => memberIds.Contains(user.Id) && user.PerfilId == Perfil.MedicosId)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        if (validMedicalUsers.Count != memberIds.Count)
        {
            throw new InvalidOperationException("Selecione apenas medicos validos para o grupo.");
        }
    }

    public static IReadOnlyCollection<int> NormalizeMemberIds(IEnumerable<int> medicoUserIds)
    {
        return medicoUserIds
            .Distinct()
            .ToArray();
    }

    public static void SyncMembers(GrupoMedico group, IReadOnlyCollection<int> nextUserIds, DateTime now)
    {
        var nextUserIdSet = nextUserIds.ToHashSet();
        var currentMembers = group.Membros.ToDictionary(member => member.UserId);

        foreach (var member in group.Membros.Where(member => !nextUserIdSet.Contains(member.UserId)).ToList())
        {
            group.Membros.Remove(member);
        }

        foreach (var userId in nextUserIds)
        {
            if (currentMembers.ContainsKey(userId))
            {
                continue;
            }

            group.Membros.Add(new GrupoMedicoUsuario
            {
                ClinicaId = group.ClinicaId,
                GrupoMedicoId = group.Id,
                UserId = userId,
                DataCadastro = now
            });
        }
    }

    public static Task<GrupoMedicoDto> GetDtoAsync(IAppDbContext context, int id, CancellationToken cancellationToken)
    {
        return context.GruposMedicos
            .AsNoTracking()
            .Where(group => group.Id == id)
            .Select(group => new GrupoMedicoDto
            {
                Id = group.Id,
                Nome = group.Nome,
                Ativo = group.Ativo,
                DataCadastro = group.DataCadastro,
                DataAtualizacao = group.DataAtualizacao,
                MembrosCount = group.Membros.Count,
                Membros = group.Membros
                    .OrderBy(member => member.User.Nome)
                    .Select(member => new GrupoMedicoMembroDto
                    {
                        UserId = member.UserId,
                        Nome = member.User.Nome,
                        Email = member.User.Email
                    })
                    .ToList()
            })
            .FirstAsync(cancellationToken);
    }
}
