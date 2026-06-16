using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.GruposMedicos.Queries;

public class GetAllGruposMedicosQueryHandler : IRequestHandler<GetAllGruposMedicosQuery, PagedResult<GrupoMedicoDto>>
{
    private readonly IAppDbContext _context;

    public GetAllGruposMedicosQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<GrupoMedicoDto>> Handle(GetAllGruposMedicosQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var search = request.Search?.Trim();

        var query = _context.GruposMedicos
            .AsNoTracking()
            .Include(group => group.Membros)
            .ThenInclude(member => member.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(group =>
                group.Nome.Contains(search)
                || group.Membros.Any(member => member.User.Nome.Contains(search) || member.User.Email.Contains(search)));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        query = ApplyOrdering(query, request.SortBy, request.SortDirection);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDtoExpression())
            .ToListAsync(cancellationToken);

        return new PagedResult<GrupoMedicoDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
        };
    }

    private static IQueryable<GrupoMedico> ApplyOrdering(IQueryable<GrupoMedico> query, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "recent" : sortBy.Trim().ToLowerInvariant();
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSortBy switch
        {
            "nome" => isDescending
                ? query.OrderByDescending(group => group.Nome).ThenByDescending(group => group.Id)
                : query.OrderBy(group => group.Nome).ThenBy(group => group.Id),
            "membros" => isDescending
                ? query.OrderByDescending(group => group.Membros.Count).ThenBy(group => group.Nome).ThenBy(group => group.Id)
                : query.OrderBy(group => group.Membros.Count).ThenBy(group => group.Nome).ThenBy(group => group.Id),
            "ativo" => isDescending
                ? query.OrderByDescending(group => group.Ativo).ThenBy(group => group.Nome).ThenBy(group => group.Id)
                : query.OrderBy(group => group.Ativo).ThenBy(group => group.Nome).ThenBy(group => group.Id),
            _ => isDescending
                ? query.OrderByDescending(group => group.DataAtualizacao ?? group.DataCadastro).ThenBy(group => group.Nome).ThenBy(group => group.Id)
                : query.OrderBy(group => group.DataAtualizacao ?? group.DataCadastro).ThenBy(group => group.Nome).ThenBy(group => group.Id),
        };
    }

    internal static System.Linq.Expressions.Expression<Func<GrupoMedico, GrupoMedicoDto>> ToDtoExpression()
    {
        return group => new GrupoMedicoDto
        {
            Id = group.Id,
            Nome = group.Nome,
            Ativo = group.Ativo,
            DataCadastro = group.DataCadastro,
            DataAtualizacao = group.DataAtualizacao,
            MembrosCount = group.Membros.Count,
            Membros = group.Membros
                .OrderBy(member => member.User.Nome)
                .Select(member => new GrupoMedicoMembroDto
                {
                    UserId = member.UserId,
                    Nome = member.User.Nome,
                    Email = member.User.Email
                })
                .ToList()
        };
    }
}

public class GetGrupoMedicoByIdQueryHandler : IRequestHandler<GetGrupoMedicoByIdQuery, GrupoMedicoDto?>
{
    private readonly IAppDbContext _context;

    public GetGrupoMedicoByIdQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public Task<GrupoMedicoDto?> Handle(GetGrupoMedicoByIdQuery request, CancellationToken cancellationToken)
    {
        return _context.GruposMedicos
            .AsNoTracking()
            .Where(group => group.Id == request.Id)
            .Select(GetAllGruposMedicosQueryHandler.ToDtoExpression())
            .FirstOrDefaultAsync(cancellationToken)!;
    }
}

public class GetScopedMedicalUsersQueryHandler : IRequestHandler<GetScopedMedicalUsersQuery, List<MedicalUserOptionDto>>
{
    private readonly IAppDbContext _context;

    public GetScopedMedicalUsersQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public Task<List<MedicalUserOptionDto>> Handle(GetScopedMedicalUsersQuery request, CancellationToken cancellationToken)
    {
        return MedicalGroupScope.BuildScopedMedicalUsersQuery(_context, request.CurrentPerfilId, request.CurrentUserId)
            .OrderBy(user => user.Nome)
            .ThenBy(user => user.Id)
            .Select(user => new MedicalUserOptionDto
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email
            })
            .ToListAsync(cancellationToken);
    }
}
