using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, PagedResult<UserDto>>
{
    private readonly IUserSearchDbContext _context;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;
    private readonly bool _supportsFullTextSearch;

    public GetAllUsersQueryHandler(
        IUserSearchDbContext context,
        ILogger<GetAllUsersQueryHandler> logger,
        IFullTextSearchCapability? fullTextSearchCapability = null)
    {
        _context = context;
        _logger = logger;
        _supportsFullTextSearch = fullTextSearchCapability?.IsSupported == true;
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

            var query = _context.Users
                .AsNoTracking()
                .Where(user => user.PerfilId != Perfil.PacientesId);
            if (request.CurrentUser?.IsEquipe == true)
            {
                var equipeId = request.CurrentUser.EquipeId
                    ?? throw new UnauthorizedAccessException("Equipe ausente na sessao");
                query = query.Where(user => _context.EquipeMembros.Any(membro => membro.EquipeId == equipeId
                    && membro.UserId == user.Id
                    && membro.Ativo));
            }
            query = ApplyFilters(query, request.ProfileId, search, digits, _supportsFullTextSearch);

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
        string digits,
        bool supportsFullTextSearch)
    {
        if (profileId.HasValue)
        {
            query = query.Where(u => u.PerfilId == profileId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var condition = FullTextSearchTermBuilder.BuildPrefixCondition(search);
            query = supportsFullTextSearch && condition != null
                ? query.Where(u =>
                    FullTextSearch.Contains(u.Nome, condition)
                    || u.Email.Contains(search)
                    || u.Telefone.Contains(search)
                    || u.Perfil.Nome.Contains(search)
                    || (!string.IsNullOrEmpty(digits) && u.Cpf != null && u.Cpf.Contains(digits))
                    || (!string.IsNullOrEmpty(digits) && u.Telefone.Contains(digits)))
                : query.Where(u =>
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
