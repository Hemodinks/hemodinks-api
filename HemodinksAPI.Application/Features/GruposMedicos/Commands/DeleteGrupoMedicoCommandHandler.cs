using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class DeleteGrupoMedicoCommandHandler : IRequestHandler<DeleteGrupoMedicoCommand>
{
    private readonly IMedicalGroupFeatureDbContext _context;

    public DeleteGrupoMedicoCommandHandler(IMedicalGroupFeatureDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.GruposMedicos
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (group == null)
        {
            throw new ResourceNotFoundException("Grupo medico nao encontrado.");
        }

        _context.GruposMedicos.Remove(group);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
