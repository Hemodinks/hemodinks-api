using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public class GetAllPacientesQueryHandler : IRequestHandler<GetAllPacientesQuery, PagedResult<PacienteDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllPacientesQueryHandler> _logger;
    private readonly bool _supportsFullTextSearch;

    public GetAllPacientesQueryHandler(
        IAppDbContext context,
        ILogger<GetAllPacientesQueryHandler> logger,
        IFullTextSearchCapability? fullTextSearchCapability = null)
    {
        _context = context;
        _logger = logger;
        _supportsFullTextSearch = fullTextSearchCapability?.IsSupported == true;
    }

    public async Task<PagedResult<PacienteDto>> Handle(GetAllPacientesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());
            var canUseAdminFilters = Perfil.IsAdministradorOuSuper(request.CurrentPerfilId);
            var medico = canUseAdminFilters ? PacienteQueryOrdering.TrimOptional(request.Medico) : null;
            var convenio = canUseAdminFilters ? PacienteQueryOrdering.TrimOptional(request.Convenio) : null;
            var medicoUserIds = ParseIds(request.MedicoUserIds);
            var convenioIds = ParseIds(request.ConvenioIds);
            var procedimento = PacienteQueryOrdering.TrimOptional(request.Procedimento);

            var query = _context.Pacientes.AsNoTracking();
            query = PacienteAccess.ApplyScope(_context, query, request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId);
            query = PacienteFilters.ApplyFilters(
                query,
                search,
                digits,
                medico,
                convenio,
                procedimento,
                medicoUserIds,
                convenioIds,
                request.DataInicio,
                request.DataFinal,
                request.DataSolicitacaoInicio,
                request.DataSolicitacaoFinal,
                _supportsFullTextSearch);

            var totalItems = await query.CountAsync(cancellationToken);
            query = PacienteQueryOrdering.ApplyOrdering(query, request.SortBy, request.SortDirection);

            var pacientes = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(PacienteProjection.ToPacienteDto(request.CurrentUserId))
                .ToListAsync(cancellationToken);

            foreach (var paciente in pacientes)
            {
                PacienteMapper.NormalizeProcedureCodes(paciente);
            }

            return new PagedResult<PacienteDto>
            {
                Items = pacientes,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar pacientes");
            throw;
        }
    }

    private static int[] ParseIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
    }

}
