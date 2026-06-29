using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, PagedResult<UserDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;

    public GetAllUsersQueryHandler(IAppDbContext context, ILogger<GetAllUsersQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<UserDto>> Handle(GetAllUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());

            _logger.LogInformation("Buscando usuarios. Pagina: {Page}, Tamanho: {PageSize}", page, pageSize);

            var query = _context.Users.AsNoTracking();
            query = ApplyFilters(query, request.ProfileId, search, digits);

            var totalItems = await query.CountAsync(cancellationToken);
            query = UserQueryOrdering.ApplyOrdering(query, request.SortBy, request.SortDirection);

            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(UserQueryMapper.ToListItemProjection())
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Encontrados {Count} usuarios na pagina de {Total} registros", users.Count, totalItems);

            return new PagedResult<UserDto>
            {
                Items = users,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuarios");
            throw;
        }
    }

    private static IQueryable<User> ApplyFilters(
        IQueryable<User> query,
        int? profileId,
        string? search,
        string digits)
    {
        if (profileId.HasValue)
        {
            query = query.Where(u => u.PerfilId == profileId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Nome.Contains(search)
                || u.Email.Contains(search)
                || u.Telefone.Contains(search)
                || u.Perfil.Nome.Contains(search)
                || (!string.IsNullOrEmpty(digits) && u.Cpf != null && u.Cpf.Contains(digits))
                || (!string.IsNullOrEmpty(digits) && u.Telefone.Contains(digits)));
        }

        return query;
    }
}
