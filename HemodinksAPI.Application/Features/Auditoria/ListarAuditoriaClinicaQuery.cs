using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Auditoria;

public sealed record ListarAuditoriaClinicaQuery(int Page, int PageSize, string? Recurso, string? RecursoId)
    : IRequest<PagedResult<AuditoriaClinicaItemDto>>;

public sealed record AuditoriaClinicaItemDto(long Id, DateTime DataUtc, int? UsuarioId, int? EquipeOperadorId,
    string Acao, string Recurso, string? RecursoId, int StatusCode, bool Sucesso, string? IpTruncado);

// O filtro global de tenant limita a trilha a clinica do usuario autenticado (#368).
public sealed class ListarAuditoriaClinicaQueryHandler(IClinicAuditDbContext db)
    : IRequestHandler<ListarAuditoriaClinicaQuery, PagedResult<AuditoriaClinicaItemDto>>
{
    public async Task<PagedResult<AuditoriaClinicaItemDto>> Handle(ListarAuditoriaClinicaQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new BusinessRuleException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");

        var query = db.AuditoriasClinica.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Recurso))
            query = query.Where(item => item.Recurso == request.Recurso.Trim());
        if (!string.IsNullOrWhiteSpace(request.RecursoId))
            query = query.Where(item => item.RecursoId == request.RecursoId.Trim());

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.DataUtc).ThenByDescending(item => item.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new AuditoriaClinicaItemDto(item.Id, item.DataUtc, item.UsuarioId, item.EquipeOperadorId,
                item.Acao, item.Recurso, item.RecursoId, item.StatusCode, item.Sucesso, item.IpTruncado))
            .ToListAsync(ct);
        return new PagedResult<AuditoriaClinicaItemDto>(items, request.Page, request.PageSize, total);
    }
}
