using HemodinksAPI.Application.Data;
using MediatR;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class CreatePacienteObservacaoCommandHandler : IRequestHandler<CreatePacienteObservacaoCommand, CreatePacienteObservacaoResult>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<CreatePacienteObservacaoCommandHandler> _logger;

    public CreatePacienteObservacaoCommandHandler(
        IAppDbContext context,
        ILogger<CreatePacienteObservacaoCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CreatePacienteObservacaoResult> Handle(CreatePacienteObservacaoCommand request, CancellationToken cancellationToken)
    {
        var paciente = await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var destinatarioIds = request.ObservacaoPaiId.HasValue
            ? await PacienteObservacaoRecipients.ResolveReplyRecipientsAsync(_context, request, cancellationToken)
            : await PacienteObservacaoRecipients.ResolveRootRecipientsAsync(_context, request, paciente, cancellationToken);

        if (destinatarioIds.Count == 0)
        {
            throw new InvalidOperationException("Nao foi encontrado nenhum destinatario para a observacao.");
        }

        var observacoes = destinatarioIds
            .Distinct()
            .Where(userId => userId != request.CurrentUserId)
            .Select(destinatarioId => PacienteObservacaoMapper.ToEntity(request, paciente, destinatarioId))
            .ToList();

        if (observacoes.Count == 0)
        {
            throw new InvalidOperationException("Nao foi encontrado nenhum destinatario valido para a observacao.");
        }

        _context.Observacoes.AddRange(observacoes);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Observacoes criadas para paciente {PacienteId}: {Count}", paciente.Id, observacoes.Count);

        return new CreatePacienteObservacaoResult
        {
            PacienteId = paciente.Id,
            CreatedCount = observacoes.Count
        };
    }
}
