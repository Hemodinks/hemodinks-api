using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Cbhpm.Queries;

public class GetCbhpmGeralQueryHandler : IRequestHandler<GetCbhpmGeralQuery, PagedResult<CbhpmGeralDto>>
{
    private const string LikeEscapeCharacter = "\\";

    private readonly IAppDbContext _context;
    private readonly ILogger<GetCbhpmGeralQueryHandler> _logger;

    public GetCbhpmGeralQueryHandler(IAppDbContext context, ILogger<GetCbhpmGeralQueryHandler> logger)
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
            IQueryable<CbhpmGeral> query = _context.CbhpmGeral.AsNoTracking();

            var codigo = CbhpmQueryRules.TrimOptional(request.Codigo);
            if (codigo != null)
            {
                query = ApplyCodigoFilter(query, codigo);
            }

            var procedimento = CbhpmQueryRules.TrimOptional(request.Procedimento);
            if (procedimento != null)
            {
                query = ApplyProcedimentoFilter(query, procedimento);
            }

            var porte = CbhpmQueryRules.TrimOptional(request.Porte);
            if (porte != null)
            {
                var porteUpper = porte.ToUpperInvariant();
                query = query.Where(item =>
                    item.Porte != null
                    && item.Porte.ToUpper() == porteUpper);
            }

            var search = CbhpmQueryRules.TrimOptional(request.Search);
            if (search != null)
            {
                query = ApplySearchFilter(query, search);
            }

            query = ApplyOrdering(query, request.SortBy, request.SortDirection);
            var totalItems = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new CbhpmGeralDto
                {
                    Id = item.Id,
                    Codigo = CbhpmCodigoUtils.Normalize(item.Codigo),
                    Procedimento = item.Procedimento,
                    Porte = item.Porte,
                    CustoOperacional = item.CustoOperacional,
                    ValorReferencia = item.ValorReferencia,
                    Capitulo = item.Capitulo,
                    Grupo = item.Grupo
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

    private static IQueryable<CbhpmGeral> ApplyCodigoFilter(IQueryable<CbhpmGeral> query, string codigo)
    {
        var originalPattern = BuildContainsLikePattern(codigo);
        var normalizedCodigo = CbhpmCodigoUtils.NormalizeOptional(codigo);

        if (normalizedCodigo == null)
        {
            return query.Where(item =>
                EF.Functions.Like(item.Codigo, originalPattern, LikeEscapeCharacter));
        }

        var normalizedPattern = BuildContainsLikePattern(normalizedCodigo);

        return query.Where(item =>
            EF.Functions.Like(item.Codigo, originalPattern, LikeEscapeCharacter)
            || EF.Functions.Like(
                item.Codigo.Replace(".", string.Empty).Replace("-", string.Empty),
                normalizedPattern,
                LikeEscapeCharacter));
    }

    private static IQueryable<CbhpmGeral> ApplyProcedimentoFilter(IQueryable<CbhpmGeral> query, string procedimento)
    {
        var procedimentoPattern = BuildContainsLikePattern(procedimento.ToUpperInvariant());

        return query.Where(item =>
            EF.Functions.Like(item.Procedimento.ToUpper(), procedimentoPattern, LikeEscapeCharacter)
            || (item.Grupo != null
                && EF.Functions.Like(item.Grupo.ToUpper(), procedimentoPattern, LikeEscapeCharacter)));
    }

    private static IQueryable<CbhpmGeral> ApplySearchFilter(IQueryable<CbhpmGeral> query, string search)
    {
        var searchPattern = BuildContainsLikePattern(search.ToUpperInvariant());
        var codigoPattern = BuildContainsLikePattern(search);
        var normalizedSearch = CbhpmCodigoUtils.NormalizeOptional(search);
        var normalizedCodigoPattern = normalizedSearch != null
            ? BuildContainsLikePattern(normalizedSearch)
            : null;

        return query.Where(item =>
            EF.Functions.Like(item.Codigo, codigoPattern, LikeEscapeCharacter)
            || (normalizedCodigoPattern != null
                && EF.Functions.Like(
                    item.Codigo.Replace(".", string.Empty).Replace("-", string.Empty),
                    normalizedCodigoPattern,
                    LikeEscapeCharacter))
            || EF.Functions.Like(item.Procedimento.ToUpper(), searchPattern, LikeEscapeCharacter)
            || (item.Porte != null
                && EF.Functions.Like(item.Porte.ToUpper(), searchPattern, LikeEscapeCharacter))
            || (item.Grupo != null
                && EF.Functions.Like(item.Grupo.ToUpper(), searchPattern, LikeEscapeCharacter)));
    }

    private static IQueryable<CbhpmGeral> ApplyOrdering(IQueryable<CbhpmGeral> query, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSortBy switch
        {
            "procedimento" => isDescending
                ? query.OrderByDescending(item => item.Procedimento).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Procedimento).ThenBy(item => item.Id),
            "porte" => isDescending
                ? query.OrderByDescending(item => item.Porte).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Porte).ThenBy(item => item.Id),
            "valorreferencia" => isDescending
                ? query.OrderByDescending(item => item.ValorReferencia ?? decimal.MinValue).ThenByDescending(item => item.Codigo).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.ValorReferencia ?? decimal.MinValue).ThenBy(item => item.Codigo).ThenBy(item => item.Id),
            _ => isDescending
                ? query.OrderByDescending(item => item.Codigo).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.Codigo).ThenBy(item => item.Id),
        };
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "codigo"
            : sortBy.Trim().ToLowerInvariant();
    }

    private static string BuildContainsLikePattern(string value)
    {
        return $"%{EscapeLikePattern(value)}%";
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}

internal static class CbhpmQueryRules
{
    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
