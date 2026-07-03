using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Application.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class UpdateGrupoMedicoCommandHandler : IRequestHandler<UpdateGrupoMedicoCommand, GrupoMedicoDto>
{
    private readonly IAppDbContext _context;
    private readonly IClinicaContext _clinicaContext;

    public UpdateGrupoMedicoCommandHandler(IAppDbContext context, IClinicaContext clinicaContext)
    {
        _context = context;
        _clinicaContext = clinicaContext;
    }

    public async Task<GrupoMedicoDto> Handle(UpdateGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        _clinicaContext.GetRequiredClinicaId();
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
