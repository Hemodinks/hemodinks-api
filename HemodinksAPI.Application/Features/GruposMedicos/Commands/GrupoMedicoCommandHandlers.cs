using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class CreateGrupoMedicoCommandHandler : IRequestHandler<CreateGrupoMedicoCommand, GrupoMedicoDto>
{
    private readonly IAppDbContext _context;

    public CreateGrupoMedicoCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<GrupoMedicoDto> Handle(CreateGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var nome = request.Nome.Trim();
        await GrupoMedicoRules.ValidateAsync(_context, null, nome, request.MedicoUserIds, cancellationToken);

        var group = new GrupoMedico
        {
            Nome = nome,
            Ativo = request.Ativo,
            DataCadastro = DateTime.UtcNow,
            Membros = request.MedicoUserIds
                .Distinct()
                .Select(userId => new GrupoMedicoUsuario
                {
                    UserId = userId,
                    DataCadastro = DateTime.UtcNow
                })
                .ToList()
        };

        _context.GruposMedicos.Add(group);
        await _context.SaveChangesAsync(cancellationToken);

        return await GrupoMedicoRules.GetDtoAsync(_context, group.Id, cancellationToken);
    }
}

public class UpdateGrupoMedicoCommandHandler : IRequestHandler<UpdateGrupoMedicoCommand, GrupoMedicoDto>
{
    private readonly IAppDbContext _context;

    public UpdateGrupoMedicoCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<GrupoMedicoDto> Handle(UpdateGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.GruposMedicos
            .Include(item => item.Membros)
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (group == null)
        {
            throw new KeyNotFoundException("Grupo medico nao encontrado.");
        }

        var nome = request.Nome.Trim();
        await GrupoMedicoRules.ValidateAsync(_context, group.Id, nome, request.MedicoUserIds, cancellationToken);

        group.Nome = nome;
        group.Ativo = request.Ativo;
        group.DataAtualizacao = DateTime.UtcNow;

        var nextUserIds = request.MedicoUserIds.Distinct().ToHashSet();
        var currentMembers = group.Membros.ToDictionary(member => member.UserId);

        foreach (var member in group.Membros.Where(member => !nextUserIds.Contains(member.UserId)).ToList())
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
                GrupoMedicoId = group.Id,
                UserId = userId,
                DataCadastro = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await GrupoMedicoRules.GetDtoAsync(_context, group.Id, cancellationToken);
    }
}

public class DeleteGrupoMedicoCommandHandler : IRequestHandler<DeleteGrupoMedicoCommand>
{
    private readonly IAppDbContext _context;

    public DeleteGrupoMedicoCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.GruposMedicos
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (group == null)
        {
            throw new KeyNotFoundException("Grupo medico nao encontrado.");
        }

        _context.GruposMedicos.Remove(group);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

internal static class GrupoMedicoRules
{
    public static async Task ValidateAsync(
        IAppDbContext context,
        int? currentGroupId,
        string nome,
        IEnumerable<int> medicoUserIds,
        CancellationToken cancellationToken)
    {
        var duplicateName = await context.GruposMedicos
            .AsNoTracking()
            .AnyAsync(group => group.Nome == nome && (!currentGroupId.HasValue || group.Id != currentGroupId.Value), cancellationToken);

        if (duplicateName)
        {
            throw new InvalidOperationException("Ja existe um grupo medico com esse nome.");
        }

        var memberIds = medicoUserIds.Distinct().ToList();
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
