using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

public class GetAllFaturamentosMedicosQueryHandler : IRequestHandler<GetAllFaturamentosMedicosQuery, PagedResult<PacienteDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllFaturamentosMedicosQueryHandler> _logger;
    private readonly bool _supportsFullTextSearch;

    public GetAllFaturamentosMedicosQueryHandler(
        IAppDbContext context,
        ILogger<GetAllFaturamentosMedicosQueryHandler> logger,
        IFullTextSearchCapability? fullTextSearchCapability = null)
    {
        _context = context;
        _logger = logger;
        _supportsFullTextSearch = fullTextSearchCapability?.IsSupported == true;
    }

    public async Task<PagedResult<PacienteDto>> Handle(GetAllFaturamentosMedicosQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());

            var query = FaturamentoMedicoScope.ApplyScope(
                _context,
                _context.Pacientes.AsNoTracking(),
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentEquipeId);

            query = FaturamentoMedicoFilters.ApplyFilters(
                query,
                request.CurrentPerfilId,
                search,
                digits,
                request.Medico,
                request.Convenio,
                request.Procedimento,
                request.CompetenciaInicio,
                request.CompetenciaFinal,
                _supportsFullTextSearch);

            var totalItems = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(p => p.Data ?? p.User.DataAtualizacao ?? p.User.DataCadastro)
                .ThenBy(p => p.NomePaciente)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(FaturamentoMedicoProjection.ToPacienteDto())
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                PacienteMapper.NormalizeProcedureCodes(item);
            }

            return new PagedResult<PacienteDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar faturamentos medicos");
            throw;
        }
    }
}
