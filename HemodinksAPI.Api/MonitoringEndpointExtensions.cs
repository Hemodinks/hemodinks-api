using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

public static class MonitoringEndpointExtensions
{
    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        app.MapGet("/api/monitoramento/erros", GetErrors)
            .WithTags("Monitoramento")
            .WithSummary("Listar erros técnicos")
            .WithDescription("Retorna somente eventos de erro. Administradores visualizam a própria clínica; o SuperAdministrador visualiza todas.")
            .RequireAuthorization("Administrador");

        app.MapDelete("/api/monitoramento/erros", ClearErrors)
            .WithTags("Monitoramento")
            .WithSummary("Limpar erros técnicos")
            .WithDescription("Oculta os erros existentes no escopo do administrador sem interromper a gravação de novos eventos.")
            .RequireAuthorization("Administrador");
    }

    private static IResult GetErrors(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        int page = 1,
        int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var isSuperAdministrator = httpContext.User.FindFirstValue("perfilId") == Perfil.SuperAdministradorId.ToString();
        int? clinicId = null;
        if (!isSuperAdministrator)
        {
            if (!int.TryParse(httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var parsedClinicId))
            {
                return Results.Forbid();
            }

            clinicId = parsedClinicId;
        }

        var reader = new MonitoringLogReader(Path.Combine(environment.ContentRootPath, "logs"));
        return Results.Ok(reader.Read(page, pageSize, clinicId));
    }

    private static async Task<IResult> ClearErrors(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var isSuperAdministrator = httpContext.User.FindFirstValue("perfilId") == Perfil.SuperAdministradorId.ToString();
        int? clinicId = null;
        if (!isSuperAdministrator)
        {
            if (!int.TryParse(httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var parsedClinicId))
            {
                return Results.Forbid();
            }

            clinicId = parsedClinicId;
        }

        var reader = new MonitoringLogReader(Path.Combine(environment.ContentRootPath, "logs"));
        var clearedAt = await reader.ClearAsync(clinicId, cancellationToken);
        return Results.Ok(new MonitoringClearResult(clearedAt));
    }
}

public sealed record MonitoringClearResult(DateTimeOffset ClearedAt);

public sealed record MonitoringErrorPage(
    IReadOnlyList<MonitoringErrorItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record MonitoringErrorItem(
    DateTimeOffset Timestamp,
    string Module,
    IReadOnlyList<string> ClassFlow,
    string Method,
    int? Line,
    string TechnicalDescription,
    string UserName,
    string UserEmail,
    string? Query,
    string? DatabaseOperation,
    string? RequestId);

public sealed class MonitoringLogReader
{
    public const string ErrorFilePattern = "hemodinks-errors-.json";
    private const int MaximumBufferedEvents = 10_000;
    private const string ClearStateFileName = "monitoring-clear-state.json";
    private static readonly SemaphoreSlim ClearStateLock = new(1, 1);
    private static readonly Regex StackFramePattern = new(
        @"at\s+(?<type>[\w.+`]+)\.(?<method>[^\s(]+)\([^\r\n]*\)(?:\s+in\s+[^\r\n]+:line\s+(?<line>\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SqlOperationPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string _logDirectory;

    public MonitoringLogReader(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public MonitoringErrorPage Read(int page, int pageSize, int? clinicId)
    {
        var events = new Queue<MonitoringErrorItem>(MaximumBufferedEvents);
        var clearState = ReadClearState();

        if (Directory.Exists(_logDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "hemodinks-errors-*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                ReadFile(file, clinicId, clearState, events);
            }
        }

        var ordered = events.OrderByDescending(item => item.Timestamp).ToList();
        var totalItems = ordered.Count;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new MonitoringErrorPage(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<DateTimeOffset> ClearAsync(int? clinicId, CancellationToken cancellationToken)
    {
        var clearedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(_logDirectory);
        await ClearStateLock.WaitAsync(cancellationToken);
        try
        {
            var state = ReadClearState();
            if (clinicId.HasValue)
            {
                state.Clinics[clinicId.Value] = clearedAt;
            }
            else
            {
                state.Global = clearedAt;
            }

            var statePath = Path.Combine(_logDirectory, ClearStateFileName);
            var temporaryPath = $"{statePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(state), cancellationToken);
                File.Move(temporaryPath, statePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            ClearStateLock.Release();
        }

        return clearedAt;
    }

    private static void ReadFile(
        string path,
        int? clinicId,
        MonitoringClearState clearState,
        Queue<MonitoringErrorItem> events)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (TryParse(line, clinicId, clearState, out var item))
                {
                    if (events.Count == MaximumBufferedEvents)
                    {
                        events.Dequeue();
                    }

                    events.Enqueue(item);
                }
            }
        }
        catch (IOException)
        {
            // A gravação pode trocar o arquivo de rolling durante a leitura; a próxima atualização tenta novamente.
        }
        catch (UnauthorizedAccessException)
        {
            // Uma falha de permissão em um arquivo não deve indisponibilizar a API.
        }
    }

    private static bool TryParse(
        string json,
        int? clinicId,
        MonitoringClearState clearState,
        out MonitoringErrorItem item)
    {
        item = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryReadTimestamp(root, out var timestamp)
                || !root.TryGetProperty("Level", out var level)
                || !string.Equals(level.GetString(), "Error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var properties = root.TryGetProperty("Properties", out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.Object
                ? propertyElement
                : default;
            var eventClinicId = ReadProperty(properties, "ClinicId");
            if (clinicId.HasValue && eventClinicId != clinicId.Value.ToString())
            {
                return false;
            }

            var effectiveCutoff = clearState.Global;
            if (int.TryParse(eventClinicId, out var parsedEventClinicId)
                && clearState.Clinics.TryGetValue(parsedEventClinicId, out var clinicCutoff)
                && (!effectiveCutoff.HasValue || clinicCutoff > effectiveCutoff.Value))
            {
                effectiveCutoff = clinicCutoff;
            }

            if (effectiveCutoff.HasValue && timestamp <= effectiveCutoff.Value)
            {
                return false;
            }

            var exception = ReadRootString(root, "Exception");
            var sourceContext = ReadProperty(properties, "SourceContext") ?? string.Empty;
            var frames = ParseFrames(exception, sourceContext);
            var firstFrame = frames.FirstOrDefault();
            var query = ReadFirstProperty(properties, "CommandText", "Query", "Sql");
            var operationMatch = string.IsNullOrWhiteSpace(query) ? null : SqlOperationPattern.Match(query);
            var renderedMessage = ReadRootString(root, "RenderedMessage")
                ?? ReadRootString(root, "MessageTemplate")
                ?? "Erro sem descrição.";

            item = new MonitoringErrorItem(
                timestamp,
                ResolveModule(firstFrame?.ClassName ?? sourceContext),
                frames.Select(frame => frame.ClassName).ToList(),
                firstFrame?.Method ?? ResolveSourceMethod(sourceContext),
                firstFrame?.Line,
                ResolveTechnicalDescription(renderedMessage),
                string.Empty,
                HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(
                    ReadProperty(properties, "UserEmail")),
                null,
                operationMatch is { Success: true } ? operationMatch.Value.ToUpperInvariant() : null,
                ReadFirstProperty(properties, "RequestId", "TraceIdentifier"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadTimestamp(JsonElement root, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return root.TryGetProperty("Timestamp", out var element)
            && DateTimeOffset.TryParse(element.GetString(), out timestamp);
    }

    private static List<StackFrameInfo> ParseFrames(string? exception, string sourceContext)
    {
        var frames = new List<StackFrameInfo>();
        if (!string.IsNullOrWhiteSpace(exception))
        {
            foreach (Match match in StackFramePattern.Matches(exception))
            {
                var className = match.Groups["type"].Value;
                if (!className.StartsWith("HemodinksAPI.", StringComparison.Ordinal))
                {
                    continue;
                }

                frames.Add(new StackFrameInfo(
                    className,
                    match.Groups["method"].Value,
                    int.TryParse(match.Groups["line"].Value, out var line) ? line : null));
            }
        }

        if (frames.Count == 0 && sourceContext.StartsWith("HemodinksAPI.", StringComparison.Ordinal))
        {
            frames.Add(new StackFrameInfo(sourceContext, ResolveSourceMethod(sourceContext), null));
        }

        return frames;
    }

    private static string ResolveModule(string source)
    {
        var segments = source.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var featuresIndex = Array.FindIndex(segments, segment => segment == "Features");
        if (featuresIndex >= 0 && featuresIndex + 1 < segments.Length)
        {
            return segments[featuresIndex + 1];
        }

        return segments.Length > 1 ? segments[1] : "API";
    }

    private static string ResolveSourceMethod(string sourceContext)
    {
        var separator = sourceContext.LastIndexOf('.');
        return separator >= 0 ? sourceContext[(separator + 1)..] : sourceContext;
    }

    private static string ResolveTechnicalDescription(string renderedMessage)
    {
        return renderedMessage.Trim('"');
    }

    private static string? ReadFirstProperty(JsonElement properties, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadProperty(properties, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string? ReadProperty(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
            }
        }

        return null;
    }

    private static string? ReadRootString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private MonitoringClearState ReadClearState()
    {
        var path = Path.Combine(_logDirectory, ClearStateFileName);
        if (!File.Exists(path)) return new MonitoringClearState();

        try
        {
            return JsonSerializer.Deserialize<MonitoringClearState>(File.ReadAllText(path))
                ?? new MonitoringClearState();
        }
        catch (JsonException)
        {
            return new MonitoringClearState();
        }
        catch (IOException)
        {
            return new MonitoringClearState();
        }
    }

    private sealed record StackFrameInfo(string ClassName, string Method, int? Line);

    private sealed class MonitoringClearState
    {
        public DateTimeOffset? Global { get; set; }
        public Dictionary<int, DateTimeOffset> Clinics { get; set; } = [];
    }
}
