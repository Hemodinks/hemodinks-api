using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

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
        var memberIds = GrupoMedicoRules.NormalizeMemberIds(request.MedicoUserIds);

        await GrupoMedicoRules.ValidateAsync(_context, group.Id, nome, memberIds, cancellationToken);

        group.Nome = nome;
        group.Ativo = request.Ativo;
        group.DataAtualizacao = DateTime.UtcNow;

        GrupoMedicoRules.SyncMembers(group, memberIds, group.DataAtualizacao.Value);

        await _context.SaveChangesAsync(cancellationToken);

        return await GrupoMedicoRules.GetDtoAsync(_context, group.Id, cancellationToken);
    }
}
