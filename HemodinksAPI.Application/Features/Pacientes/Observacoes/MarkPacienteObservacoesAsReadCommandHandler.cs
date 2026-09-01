using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class MarkPacienteObservacoesAsReadCommandHandler : IRequestHandler<MarkPacienteObservacoesAsReadCommand, MarkPacienteObservacoesAsReadResult>
{
    private readonly IPatientFeatureDbContext _context;

    public MarkPacienteObservacoesAsReadCommandHandler(IPatientFeatureDbContext context)
    {
        _context = context;
    }

    public async Task<MarkPacienteObservacoesAsReadResult> Handle(MarkPacienteObservacoesAsReadCommand request, CancellationToken cancellationToken)
    {
        await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var unread = await _context.Observacoes
            .Where(observacao =>
                observacao.PacienteId == request.PacienteId
                && observacao.DestinatarioUserId == request.CurrentUserId
                && observacao.DataLeitura == null)
            .ToListAsync(cancellationToken);

        if (unread.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var observacao in unread)
            {
                observacao.DataLeitura = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MarkPacienteObservacoesAsReadResult
        {
            PacienteId = request.PacienteId,
            UpdatedCount = unread.Count
        };
    }
}
