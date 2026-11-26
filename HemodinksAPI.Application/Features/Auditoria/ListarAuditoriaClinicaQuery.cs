using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Exceptions;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Auditoria;

// Filtros da tela geral de auditoria: os da trilha e os mesmos da tela de Pacientes - Cirurgias.
public sealed record AuditoriaClinicaFiltros
{
    public int? PacienteId { get; init; }
    public int? UsuarioId { get; init; }
    public string? Acao { get; init; }
    public DateTime? PeriodoInicio { get; init; }
    public DateTime? PeriodoFim { get; init; }
    // Esconde consultas (listar, ler, baixar...): so inclusoes, alteracoes, inativacoes etc.
    public bool? SomenteAlteracoes { get; init; }

    public string? Search { get; init; }
    public TipoCirurgia? TipoCirurgia { get; init; }
    public StatusCirurgia? StatusCirurgia { get; init; }
    public string? Medico { get; init; }
    public string? Convenio { get; init; }
    public string? Procedimento { get; init; }
    public string? MedicoUserIds { get; init; }
    public string? ConvenioIds { get; init; }
    public DateTime? DataInicio { get; init; }
    public DateTime? DataFinal { get; init; }
    public DateTime? DataSolicitacaoInicio { get; init; }
    public DateTime? DataSolicitacaoFinal { get; init; }

    internal bool HasPacienteFilters =>
        !string.IsNullOrWhiteSpace(Search) || TipoCirurgia.HasValue || StatusCirurgia.HasValue
        || !string.IsNullOrWhiteSpace(Medico) || !string.IsNullOrWhiteSpace(Convenio) || !string.IsNullOrWhiteSpace(Procedimento)
        || !string.IsNullOrWhiteSpace(MedicoUserIds) || !string.IsNullOrWhiteSpace(ConvenioIds)
        || DataInicio.HasValue || DataFinal.HasValue || DataSolicitacaoInicio.HasValue || DataSolicitacaoFinal.HasValue;
}

public sealed record ListarAuditoriaClinicaQuery(int Page, int PageSize, string? Recurso, string? RecursoId,
    AuditoriaClinicaFiltros? Filtros = null)
    : IRequest<PagedResult<AuditoriaClinicaItemDto>>;

public sealed record AuditoriaClinicaItemDto(long Id, DateTime DataUtc, int? UsuarioId, int? EquipeOperadorId,
    string Acao, string Recurso, string? RecursoId, int StatusCode, bool Sucesso, string? IpTruncado,
    string? UsuarioNome = null, string? EquipeOperadorNome = null,
    int? PacienteId = null, string? PacienteNome = null, string? Detalhe = null);

// O filtro global de tenant limita a trilha a clinica do usuario autenticado (#368). Os filtros de paciente
// rodam sobre Pacientes da mesma clinica (todas as situacoes), no mesmo contexto da requisicao.
public sealed class ListarAuditoriaClinicaQueryHandler(
    IClinicAuditDbContext db,
    IPatientFeatureDbContext? patients = null,
    IFullTextSearchCapability? fullTextSearch = null)
    : IRequestHandler<ListarAuditoriaClinicaQuery, PagedResult<AuditoriaClinicaItemDto>>
{
    // Acoes de consulta, escondidas com SomenteAlteracoes.
    internal static readonly string[] ConsultaAcoes =
    [
        "paciente.listar", "paciente.ler", "paciente.resumo-financeiro", "arquivo.baixar",
        "observacao.listar", "observacao.marcar-lidas"
    ];

    public async Task<PagedResult<AuditoriaClinicaItemDto>> Handle(ListarAuditoriaClinicaQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new BusinessRuleException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
        var filtros = request.Filtros ?? new AuditoriaClinicaFiltros();
        if (filtros.PeriodoInicio > filtros.PeriodoFim)
            throw new BusinessRuleException("O inicio do periodo deve ser anterior ao fim.");

        var query = db.AuditoriasClinica.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Recurso))
            query = query.Where(item => item.Recurso == request.Recurso.Trim());
        if (!string.IsNullOrWhiteSpace(request.RecursoId))
            query = query.Where(item => item.RecursoId == request.RecursoId.Trim());
        query = ApplyTrailFilters(query, filtros);
        if (filtros.HasPacienteFilters)
        {
            var pacienteIds = FilteredPacientes(filtros).Select(paciente => paciente.Id);
            query = query.Where(item => item.PacienteId.HasValue && pacienteIds.Contains(item.PacienteId.Value));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.DataUtc).ThenByDescending(item => item.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            // Nomes resolvidos na leitura e sob o filtro de tenant: usuario/paciente de outra clinica vem nulo.
            .Select(item => new AuditoriaClinicaItemDto(item.Id, item.DataUtc, item.UsuarioId, item.EquipeOperadorId,
                item.Acao, item.Recurso, item.RecursoId, item.StatusCode, item.Sucesso, item.IpTruncado,
                db.Users.Where(user => user.Id == item.UsuarioId).Select(user => user.Nome).FirstOrDefault(),
                db.EquipeOperadores.Where(op => op.Id == item.EquipeOperadorId).Select(op => op.User.Nome).FirstOrDefault(),
                item.PacienteId,
                db.Pacientes.Where(paciente => paciente.Id == item.PacienteId).Select(paciente => paciente.NomePaciente).FirstOrDefault(),
                item.Detalhe))
            .ToListAsync(ct);
        return new PagedResult<AuditoriaClinicaItemDto>(items, request.Page, request.PageSize, total);
    }

    private static IQueryable<AuditoriaClinica> ApplyTrailFilters(IQueryable<AuditoriaClinica> query, AuditoriaClinicaFiltros filtros)
    {
        if (filtros.PacienteId.HasValue)
            query = query.Where(item => item.PacienteId == filtros.PacienteId);
        if (filtros.UsuarioId.HasValue)
            query = query.Where(item => item.UsuarioId == filtros.UsuarioId);
        if (!string.IsNullOrWhiteSpace(filtros.Acao))
        {
            var acao = filtros.Acao.Trim();
            query = query.Where(item => item.Acao == acao);
        }
        if (filtros.PeriodoInicio.HasValue)
        {
            var inicio = filtros.PeriodoInicio.Value.Date;
            query = query.Where(item => item.DataUtc >= inicio);
        }
        if (filtros.PeriodoFim.HasValue)
        {
            var fimExclusivo = filtros.PeriodoFim.Value.Date.AddDays(1);
            query = query.Where(item => item.DataUtc < fimExclusivo);
        }
        if (filtros.SomenteAlteracoes == true)
            query = query.Where(item => !ConsultaAcoes.Contains(item.Acao));
        return query;
    }

    private IQueryable<Paciente> FilteredPacientes(AuditoriaClinicaFiltros filtros)
    {
        var source = patients ?? throw new InvalidOperationException("Filtros de paciente indisponiveis.");
        TipoCirurgiaRules.Validate(filtros.TipoCirurgia);
        StatusCirurgiaRules.Validate(filtros.StatusCirurgia);
        var search = filtros.Search?.Trim();
        var digits = PacienteFilters.GetStructuredSearchDigits(search);
        var query = source.Pacientes.AsNoTracking();
        if (filtros.TipoCirurgia.HasValue) query = query.Where(p => p.TipoCirurgia == filtros.TipoCirurgia);
        if (filtros.StatusCirurgia.HasValue) query = query.Where(p => p.StatusCirurgia == filtros.StatusCirurgia);
        return PacienteFilters.ApplyFilters(
            query,
            search,
            digits,
            PacienteQueryOrdering.TrimOptional(filtros.Medico),
            PacienteQueryOrdering.TrimOptional(filtros.Convenio),
            PacienteQueryOrdering.TrimOptional(filtros.Procedimento),
            ParseIds(filtros.MedicoUserIds),
            ParseIds(filtros.ConvenioIds),
            filtros.DataInicio,
            filtros.DataFinal,
            filtros.DataSolicitacaoInicio,
            filtros.DataSolicitacaoFinal,
            fullTextSearch?.IsSupported == true,
            digits.Length == 11 ? source.ComputeCpfLookupHashCandidates(digits) : null);
    }

    private static int[] ParseIds(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
}
