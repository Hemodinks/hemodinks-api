using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Cbhpm.Queries;

public class GetCbhpmGeralQueryHandler : IRequestHandler<GetCbhpmGeralQuery, PagedResult<CbhpmGeralDto>>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetCbhpmGeralQueryHandler> _logger;

    public GetCbhpmGeralQueryHandler(AppDbContext context, ILogger<GetCbhpmGeralQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<CbhpmGeralDto>> Handle(GetCbhpmGeralQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var query = _context.CbhpmGeral.AsNoTracking();

            var codigo = CbhpmQueryRules.TrimOptional(request.Codigo);
            if (codigo != null)
            {
                query = query.Where(item => item.Codigo.Contains(codigo));
            }

            var procedimento = CbhpmQueryRules.TrimOptional(request.Procedimento);
            if (procedimento != null)
            {
                var normalizedProcedimento = procedimento.ToUpperInvariant();
                query = query.Where(item => item.Procedimento.ToUpper().Contains(normalizedProcedimento));
            }

            var porte = CbhpmQueryRules.TrimOptional(request.Porte);
            if (porte != null)
            {
                var normalizedPorte = porte.ToUpperInvariant();
                query = query.Where(item => item.Porte != null && item.Porte.ToUpper() == normalizedPorte);
            }

            var search = CbhpmQueryRules.TrimOptional(request.Search);
            if (search != null)
            {
                var normalizedSearch = search.ToUpperInvariant();
                query = query.Where(item =>
                    item.Codigo.Contains(search)
                    || item.Procedimento.ToUpper().Contains(normalizedSearch)
                    || (item.Porte != null && item.Porte.ToUpper().Contains(normalizedSearch)));
            }

            var totalItems = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderBy(item => item.Codigo)
                .ThenBy(item => item.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new CbhpmGeralDto
                {
                    Id = item.Id,
                    Codigo = item.Codigo,
                    Procedimento = item.Procedimento,
                    Porte = item.Porte,
                    CustoOperacional = item.CustoOperacional,
                    Capitulo = item.Capitulo,
                    Grupo = item.Grupo,
                    PaginaPdf = item.PaginaPdf
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<CbhpmGeralDto>
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
            _logger.LogError(ex, "Erro ao buscar procedimentos CBHPM");
            throw;
        }
    }
}

internal static class CbhpmQueryRules
{
    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
