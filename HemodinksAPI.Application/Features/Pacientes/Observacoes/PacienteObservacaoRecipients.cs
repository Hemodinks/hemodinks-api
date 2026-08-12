using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

internal static class PacienteObservacaoRecipients
{
    public static async Task<List<int>> ResolveReplyRecipientsAsync(
        IAppDbContext context,
        CreatePacienteObservacaoCommand request,
        CancellationToken cancellationToken)
    {
        var parent = await context.Observacoes
            .AsNoTracking()
            .Where(observacao => observacao.Id == request.ObservacaoPaiId && observacao.PacienteId == request.PacienteId)
            .Select(observacao => new
            {
                observacao.Id,
                observacao.AutorUserId,
                observacao.DestinatarioUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (parent == null)
        {
            throw new InvalidOperationException("Observacao de origem nao encontrada.");
        }

        if (parent.AutorUserId == request.CurrentUserId)
        {
            return [parent.DestinatarioUserId];
        }

        if (parent.DestinatarioUserId == request.CurrentUserId)
        {
            return [parent.AutorUserId];
        }

        throw new UnauthorizedAccessException("Sem permissao para responder esta observacao.");
    }

    public static async Task<List<int>> ResolveRootRecipientsAsync(
        IAppDbContext context,
        CreatePacienteObservacaoCommand request,
        PacienteObservacaoContext paciente,
        CancellationToken cancellationToken)
    {
        if (Perfil.IsAdministradorOuSuper(request.CurrentPerfilId) || request.CurrentPerfilId == Perfil.ControllerId)
        {
            var medicalIds = new[] { paciente.MedicoUserId, paciente.MedicoAuxiliar1UserId, paciente.MedicoAuxiliar2UserId }
                .Where(userId => userId.HasValue)
                .Select(userId => userId!.Value)
                .Distinct()
                .ToList();

            if (medicalIds.Count == 0)
            {
                throw new InvalidOperationException("Selecione ao menos um medico vinculado ao paciente antes de enviar observacoes.");
            }

            return await context.Users
                .AsNoTracking()
                .Where(user => medicalIds.Contains(user.Id) && user.Ativo)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        if (request.CurrentPerfilId == Perfil.MedicosId)
        {
            return await context.Users
                .AsNoTracking()
                .Where(user =>
                    user.Ativo
                    && (Perfil.IsAdministradorOuSuper(user.PerfilId) || user.PerfilId == Perfil.ControllerId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        throw new UnauthorizedAccessException("Sem permissao para registrar observacoes.");
    }
}
