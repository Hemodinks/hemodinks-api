using HemodinksAPI.Application.Authentication;
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
            .Include(p => p.User)
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("Paciente nao encontrado");

        if (!paciente.Ativo)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        paciente.Ativo = false;
        paciente.InativadoEm = now;
        paciente.InativadoPorUserId = request.CurrentUserId;

        // Paciente inativado nao enxerga mais o proprio cadastro: o login do titular e desligado (o vinculo
        // inativo derruba sessoes e refresh na proxima requisicao). So login de perfil Paciente e afetado.
        if (paciente.User.PerfilId == Perfil.PacientesId && paciente.User.Ativo)
        {
            paciente.User.Ativo = false;
            paciente.User.DataAtualizacao = now;
            paciente.LoginBloqueadoNaInativacao = true;
            await GlobalIdentityService.SynchronizeUserAsync(_context, paciente.User, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class ReativarPacienteCommand : IRequest
{
    public int Id { get; set; }
    public int CurrentPerfilId { get; set; }
}

public class ReativarPacienteCommandHandler(IPatientFeatureDbContext context, TimeProvider? timeProvider = null)
    : IRequestHandler<ReativarPacienteCommand>
{
    public async Task Handle(ReativarPacienteCommand request, CancellationToken cancellationToken)
    {
        if (!Perfil.IsAdministradorOuSuper(request.CurrentPerfilId))
        {
            throw new UnauthorizedAccessException("Sem permissao para reativar paciente");
        }

        var paciente = await context.Pacientes
            .Include(p => p.User)
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("Paciente nao encontrado");

        if (paciente.Ativo)
        {
            return;
        }

        var inativadoEm = paciente.InativadoEm;
        paciente.Ativo = true;
        paciente.InativadoEm = null;
        paciente.InativadoPorUserId = null;

        // Religa o login so se o usuario continua exatamente como a inativacao o deixou: desligado, perfil
        // Paciente e sem nenhuma alteracao depois (DataAtualizacao gravada junto com InativadoEm). Se alguem
        // mexeu no usuario nesse meio-tempo (religou, desligou por outro motivo, editou), o login fica como esta.
        var untouchedSinceInactivation = paciente.LoginBloqueadoNaInativacao
            && !paciente.User.Ativo
            && paciente.User.PerfilId == Perfil.PacientesId
            && inativadoEm.HasValue
            && paciente.User.DataAtualizacao == inativadoEm;
        paciente.LoginBloqueadoNaInativacao = false;
        if (untouchedSinceInactivation)
        {
            paciente.User.Ativo = true;
            paciente.User.DataAtualizacao = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            await GlobalIdentityService.SynchronizeUserAsync(context, paciente.User, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
