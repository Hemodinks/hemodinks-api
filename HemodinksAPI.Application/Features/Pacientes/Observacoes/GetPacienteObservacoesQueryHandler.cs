using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class GetPacienteObservacoesQueryHandler : IRequestHandler<GetPacienteObservacoesQuery, IReadOnlyList<PacienteObservacaoDto>>
{
    private readonly IPatientFeatureDbContext _context;
    private readonly ILogger<GetPacienteObservacoesQueryHandler> _logger;

    public GetPacienteObservacoesQueryHandler(
        IPatientFeatureDbContext context,
        ILogger<GetPacienteObservacoesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PacienteObservacaoDto>> Handle(GetPacienteObservacoesQuery request, CancellationToken cancellationToken)
    {
        await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        try
        {
            return await _context.Observacoes
                .AsNoTracking()
                .Where(observacao =>
                    observacao.PacienteId == request.PacienteId
                    && (observacao.AutorUserId == request.CurrentUserId || observacao.DestinatarioUserId == request.CurrentUserId))
                .OrderByDescending(observacao => observacao.DataCadastro)
                .ThenByDescending(observacao => observacao.Id)
                .Select(PacienteObservacaoMapper.ToDtoProjection(request.CurrentUserId))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar observacoes do paciente {PacienteId}", request.PacienteId);
            throw;
        }
    }
}
