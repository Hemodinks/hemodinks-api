using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

// #369: DELETE /api/pacientes/{id} so inativa. Nenhuma linha, arquivo ou foto e removido: o prontuario
// tem guarda legal (Lei 13.787/2018, Res. CFM 1.821/2007). Remocao fisica fica para rotina administrativa.
public class DeletePacienteCommandHandler : IRequestHandler<DeletePacienteCommand>
{
    private readonly IPatientFeatureDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DeletePacienteCommandHandler(IPatientFeatureDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task Handle(DeletePacienteCommand request, CancellationToken cancellationToken)
    {
        if (!Perfil.IsAdministradorOuSuper(request.CurrentPerfilId))
        {
            throw new UnauthorizedAccessException("Sem permissao para inativar paciente");
        }

        // Filtro global de tenant: paciente de outra clinica nao e encontrado (404).
        var paciente = await _context.Pacientes
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("Paciente nao encontrado");

        if (!paciente.Ativo)
        {
            return;
        }

        paciente.Ativo = false;
        paciente.InativadoEm = _timeProvider.GetUtcNow().UtcDateTime;
        paciente.InativadoPorUserId = request.CurrentUserId;
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class ReativarPacienteCommand : IRequest
{
    public int Id { get; set; }
    public int CurrentPerfilId { get; set; }
}

public class ReativarPacienteCommandHandler(IPatientFeatureDbContext context) : IRequestHandler<ReativarPacienteCommand>
{
    public async Task Handle(ReativarPacienteCommand request, CancellationToken cancellationToken)
    {
        if (!Perfil.IsAdministradorOuSuper(request.CurrentPerfilId))
        {
            throw new UnauthorizedAccessException("Sem permissao para reativar paciente");
        }

        var paciente = await context.Pacientes
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("Paciente nao encontrado");

        if (paciente.Ativo)
        {
            return;
        }

        paciente.Ativo = true;
        paciente.InativadoEm = null;
        paciente.InativadoPorUserId = null;
        await context.SaveChangesAsync(cancellationToken);
    }
}
