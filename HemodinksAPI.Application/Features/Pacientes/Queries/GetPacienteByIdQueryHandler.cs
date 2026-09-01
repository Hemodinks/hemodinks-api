using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public class GetPacienteByIdQueryHandler : IRequestHandler<GetPacienteByIdQuery, PacienteDto?>
{
    private readonly IPatientFeatureDbContext _context;
    private readonly ILogger<GetPacienteByIdQueryHandler> _logger;

    public GetPacienteByIdQueryHandler(IPatientFeatureDbContext context, ILogger<GetPacienteByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PacienteDto?> Handle(GetPacienteByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IQueryable<Paciente> query = _context.Pacientes
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.MedicoUser)
                .Include(p => p.MedicoAuxiliar1User)
                .Include(p => p.MedicoAuxiliar2User)
                .Include(p => p.HospitalReferencia)
                .Include(p => p.ConvenioReferencia)
                .Include(p => p.OpmeFornecedorReferencia)
                .Include(p => p.FaturamentoMedico)
                .Include(p => p.Procedimentos)
                .Include(p => p.Observacoes)
                .Include(p => p.Arquivos);

            query = PacienteAccess.ApplyScope(_context, query, request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId);

            var paciente = await query
                .Where(p => p.Id == request.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (paciente == null)
            {
                return null;
            }

            var dto = PacienteMapper.ToDto(paciente);
            dto.ObservacoesNaoLidasCount = paciente.Observacoes.Count(observacao =>
                observacao.DestinatarioUserId == request.CurrentUserId
                && observacao.DataLeitura == null);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}
