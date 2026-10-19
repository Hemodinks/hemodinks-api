using System.Globalization;
using System.Text.Json;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.Exports.Commands;

internal static class FileExportSnapshotBuilder
{
    private const int MaxQueueBytes = 45_000; // Azure Queue Base64 must remain below 64 KiB.
    private const int MaxRecords = 1_000;
    private static readonly JsonSerializerOptions QueueJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] Columns = ["Id", "Paciente", "Data cirurgia", "Tipo cirurgia", "Medico", "Auxiliar 1", "Auxiliar 2", "Convenio", "Procedimento", "Honorarios cirurgiao", "Honorarios auxiliares", "Repasse medico", "Valor glosa", "Pagamento conferido"];
    private static readonly HashSet<string> PatientFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "tipoCirurgia", "medico", "convenio", "medicoUserIds", "convenioIds", "procedimento",
        "dataInicio", "dataFinal", "dataSolicitacaoInicio", "dataSolicitacaoFinal", "sortBy", "sortDirection"
    };
    private static readonly HashSet<string> BillingFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "tipoCirurgia", "medico", "convenio", "procedimento", "competenciaInicio", "competenciaFinal"
    };

    internal static void EnsureQueueLimit(FileExportQueueMessage message)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(message, QueueJson).Length > MaxQueueBytes)
            throw TooLarge();
    }

    internal static async Task<FileExportQueueMessage> CaptureAsync(ISender sender, FileExportQueueMessage message,
        CurrentUserContext user, CancellationToken ct)
    {
        var allowed = message.Resource == "pacientes" ? PatientFilters : BillingFilters;
        if (message.Filters.Keys.Any(key => !allowed.Contains(key)))
            throw new InvalidOperationException("Filtro nao suportado para esta exportacao. Use os mesmos filtros da listagem, sem paginacao.");
        var f = message.Filters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        string? Get(string key) => f.GetValueOrDefault(key);
        var tipo = ParseSurgeryType(Get("tipoCirurgia"));
        if (tipo.HasValue) f["tipoCirurgia"] = tipo.Value.ToString();
        var start = ParseDate(Get("dataInicio"), "dataInicio");
        var end = ParseDate(Get("dataFinal"), "dataFinal");
        var requestedStart = ParseDate(Get("dataSolicitacaoInicio"), "dataSolicitacaoInicio");
        var requestedEnd = ParseDate(Get("dataSolicitacaoFinal"), "dataSolicitacaoFinal");
        var competenceStart = ParseDate(Get("competenciaInicio"), "competenciaInicio", competence: true);
        var competenceEnd = ParseDate(Get("competenciaFinal"), "competenciaFinal", competence: true);
        ValidateIds(Get("medicoUserIds"));
        ValidateIds(Get("convenioIds"));
        var sortBy = Get("sortBy")?.ToLowerInvariant();
        if (sortBy != null && sortBy is not ("data" or "dataprocedimento" or "dataatendimento" or "nome" or "hospital" or "medico" or "convenio" or "auxiliares" or "status" or "arquivos" or "datacadastro" or "tipocirurgia"))
            throw new InvalidOperationException("Ordenacao de exportacao invalida.");
        var direction = Get("sortDirection")?.ToLowerInvariant();
        if (direction != null && direction is not ("asc" or "desc"))
            throw new InvalidOperationException("Direcao de ordenacao invalida. Use asc ou desc.");
        message = message with { Filters = f };
        var rows = new List<string?[]>();
        var records = new List<PacienteDto>();
        var seen = new HashSet<int>();
        int? expectedCount = null;
        for (var page = 1; ; page++)
        {
            PagedResult<PacienteDto> result;
            if (message.Resource == "pacientes")
                result = await sender.Send(new GetAllPacientesQuery
                {
                    Page = page, PageSize = 100, Search = Get("search"), TipoCirurgia = tipo,
                    Medico = Get("medico"), Convenio = Get("convenio"), MedicoUserIds = Get("medicoUserIds"),
                    ConvenioIds = Get("convenioIds"), Procedimento = Get("procedimento"), DataInicio = start, DataFinal = end,
                    DataSolicitacaoInicio = requestedStart, DataSolicitacaoFinal = requestedEnd, SortBy = sortBy, SortDirection = direction,
                    CurrentUserId = user.Id, CurrentPerfilId = user.PerfilId, CurrentEquipeId = user.EquipeId
                }, ct);
            else
                result = await sender.Send(new GetAllFaturamentosMedicosQuery
                {
                    Page = page, PageSize = 100, Search = Get("search"), TipoCirurgia = tipo,
                    Medico = Get("medico"), Convenio = Get("convenio"), Procedimento = Get("procedimento"),
                    CompetenciaInicio = competenceStart, CompetenciaFinal = competenceEnd,
                    CurrentUserId = user.Id, CurrentPerfilId = user.PerfilId, CurrentEquipeId = user.EquipeId
                }, ct);
            expectedCount ??= result.TotalItems;
            if (result.TotalItems > MaxRecords) throw TooLarge();
            if (result.TotalItems != expectedCount || result.Items.Any(item => !seen.Add(item.Id)))
                throw new InvalidOperationException("Os dados mudaram durante a exportacao. Solicite novamente.");
            records.AddRange(result.Items);
            if (message.ReportType == "completo") rows.AddRange(result.Items.Select(ToRow));
            message = message with { Snapshot = new FileExportSnapshot(Columns, rows, records.Count, Summarize(records)) };
            EnsureQueueLimit(message);
            if (records.Count == expectedCount) return message;
            if (result.Items.Count == 0 || records.Count > expectedCount || records.Count >= MaxRecords)
                throw new InvalidOperationException("Os dados mudaram durante a exportacao. Solicite novamente.");
        }
    }

    private static string?[] ToRow(PacienteDto p) =>
    [
        p.Id.ToString(CultureInfo.InvariantCulture), p.NomePaciente, (p.DataAtendimento ?? p.Data)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        p.TipoCirurgia.ToString(), p.Medico, p.MedicoAuxiliar1, p.MedicoAuxiliar2, p.Convenio,
        p.Procedimento ?? string.Join("; ", p.Procedimentos.Select(item => item.Procedimento)),
        Money(p.Faturamento?.HonorariosCirurgiao), Money(p.Faturamento?.HonorariosAuxiliares),
        Money(p.Faturamento?.RepasseMedico), Money(p.Faturamento?.ValorGlosa), p.StatusPago ? "Sim" : "Nao"
    ];

    private static List<FileExportTypeTotal> Summarize(List<PacienteDto> records) => records
        .GroupBy(p => p.TipoCirurgia).OrderBy(g => g.Key)
        .Select(g => new FileExportTypeTotal(g.Key.ToString(), g.Count(),
            Sum(g.Select(p => p.Faturamento?.HonorariosCirurgiao)), Sum(g.Select(p => p.Faturamento?.HonorariosAuxiliares)),
            Sum(g.Select(p => p.Faturamento?.RepasseMedico)), Sum(g.Select(p => p.Faturamento?.ValorGlosa)))).ToList();

    private static decimal? Sum(IEnumerable<decimal?> amounts)
    {
        var known = amounts.Where(value => value.HasValue).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    internal static string? Money(decimal? value) => value?.ToString("F2", CultureInfo.InvariantCulture);
    private static InvalidOperationException TooLarge() => new("Exportacao excede o limite seguro. Reduza o intervalo ou aplique mais filtros; nenhum registro foi exportado.");

    private static TipoCirurgia? ParseSurgeryType(string? value)
    {
        if (value == null) return null;
        if (Enum.TryParse<TipoCirurgia>(value, true, out var result) && Enum.IsDefined(result)
            && (value.Equals("Eletiva", StringComparison.OrdinalIgnoreCase) || value.Equals("Urgencia", StringComparison.OrdinalIgnoreCase))) return result;
        throw new InvalidOperationException("Tipo de cirurgia invalido. Use Eletiva ou Urgencia.");
    }

    private static void ValidateIds(string? value)
    {
        if (value != null && value.Split(',', StringSplitOptions.TrimEntries).Any(part => !int.TryParse(part, out var id) || id <= 0))
            throw new InvalidOperationException("Identificadores de filtro invalidos.");
    }

    private static DateTime? ParseDate(string? value, string key, bool competence = false)
    {
        if (value == null) return null;
        string[] formats = competence
            ? ["MM/yyyy", "M/yyyy", "MM-yyyy", "M-yyyy", "yyyy-MM", "yyyy-M", "yyyy-MM-dd", "yyyy-M-d"]
            : ["yyyy-MM-dd", "O", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)) return result;
        if (competence && (DateTime.TryParse(value, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out result)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))) return result;
        throw new InvalidOperationException($"Filtro {key} invalido.");
    }
}
