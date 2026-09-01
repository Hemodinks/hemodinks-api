using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed partial class FinanceiroFileUseCases
{
    public async Task<PagedResult<FinanceAuditItemDto>> ListAuditAsync(
        int page,
        int pageSize,
        string? resource,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new InvalidOperationException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
        var clinicId = tenant.GetRequiredClinicaId();
        var query = db.AuditoriasPlataforma.AsNoTracking()
            .Where(item => item.ClinicaId == clinicId && item.Recurso.StartsWith("financeiro:"));
        if (!string.IsNullOrWhiteSpace(resource))
            query = query.Where(item => item.Recurso == $"financeiro:{resource.Trim()}");
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.DataCadastro)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new FinanceAuditItemDto(item.Id, item.Acao, item.Recurso, item.EntidadeId,
                item.DetalhesJson, item.UserId, item.Ip, item.Sucesso, item.DataCadastro))
            .ToListAsync(cancellationToken);
        return new PagedResult<FinanceAuditItemDto>(items, page, pageSize, total);
    }

    private static void EnsureMedicalAccess(AtendimentoCirurgico atendimento, CurrentUserContext user, string message)
    {
        if (user.PerfilId == Perfil.MedicosId
            && atendimento.MedicoResponsavelId != user.Id
            && atendimento.MedicoAuxiliar1Id != user.Id
            && atendimento.MedicoAuxiliar2Id != user.Id)
            throw new UnauthorizedAccessException(message);
    }

    private static void ValidateHistoryPeriod(int? year, int? month, bool requireBoth)
    {
        if (requireBoth && (!year.HasValue || !month.HasValue))
            throw new InvalidOperationException("Ano e mês são obrigatórios.");
        if (year.HasValue && year is < 1900 or > 2100)
            throw new InvalidOperationException("O ano deve estar entre 1900 e 2100.");
        if (month.HasValue && month is < 1 or > 12)
            throw new InvalidOperationException("O mês deve estar entre 1 e 12.");
        if (month.HasValue && !year.HasValue)
            throw new InvalidOperationException("Informe o ano ao filtrar por mês.");
    }
}
