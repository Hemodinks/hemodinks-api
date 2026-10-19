using HemodinksAPI.Application.Async;
using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.Exports.Commands;

public class RequestFileExportCommandHandler : IRequestHandler<RequestFileExportCommand, RequestFileExportResponse>
{
    private static readonly HashSet<string> AllowedResources = new(StringComparer.OrdinalIgnoreCase)
    {
        "pacientes",
        "faturamentos-medicos",
        "cbhpm"
    };

    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf",
        "xlsx"
    };

    private readonly IFileExportQueue _fileExportQueue;
    private readonly ILogger<RequestFileExportCommandHandler> _logger;
    private readonly ISender _sender;

    public RequestFileExportCommandHandler(
        IFileExportQueue fileExportQueue,
        ILogger<RequestFileExportCommandHandler> logger,
        ISender sender)
    {
        _fileExportQueue = fileExportQueue;
        _logger = logger;
        _sender = sender;
    }

    public async Task<RequestFileExportResponse> Handle(RequestFileExportCommand request, CancellationToken cancellationToken)
    {
        var currentUser = request.CurrentUser
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
        var resource = NormalizeRequired(request.Resource, "Recurso de exportacao obrigatorio");
        var format = NormalizeRequired(request.Format, "Formato de exportacao obrigatorio");

        if (!AllowedResources.Contains(resource))
        {
            throw new InvalidOperationException("Recurso de exportacao invalido");
        }

        if (!AllowedFormats.Contains(format))
        {
            throw new InvalidOperationException("Formato de exportacao invalido. Use PDF ou XLSX.");
        }

        var reportType = string.IsNullOrWhiteSpace(request.ReportType) ? "completo" : request.ReportType.Trim().ToLowerInvariant();
        if (reportType is not ("completo" or "sintetico"))
            throw new InvalidOperationException("Tipo de relatorio invalido. Use completo ou sintetico.");
        if (currentUser.ClinicaId <= 0 || currentUser.IsEquipe && !currentUser.EquipeId.HasValue)
            throw new UnauthorizedAccessException("Contexto de exportacao invalido.");
        if (resource == "faturamentos-medicos" && !(currentUser.IsAdministrador || currentUser.IsController || currentUser.IsMedico || currentUser.IsEquipe))
            throw new UnauthorizedAccessException("Sem permissao para exportar faturamentos medicos.");
        if (resource == "cbhpm" && (reportType != "completo" || request.Filters?.Keys.Any(key => string.Equals(key.Trim(), "tipoCirurgia", StringComparison.OrdinalIgnoreCase)) == true))
            throw new InvalidOperationException("CBHPM nao possui classificacao cirurgica ou relatorio sintetico.");

        var requestedAt = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var message = new FileExportQueueMessage(
            jobId,
            resource,
            format,
            currentUser.Id,
            currentUser.PerfilId,
            currentUser.ClinicaId,
            requestedAt,
            NormalizeFilters(request.Filters),
            reportType);

        FileExportSnapshotBuilder.EnsureQueueLimit(message);
        if (resource != "cbhpm")
            message = await FileExportSnapshotBuilder.CaptureAsync(_sender, message, currentUser, cancellationToken);

        await _fileExportQueue.EnqueueAsync(message, cancellationToken);

        _logger.LogInformation(
            "Exportacao {JobId} enfileirada para {Resource} em {Format}",
            jobId,
            resource,
            format);

        return new RequestFileExportResponse
        {
            JobId = jobId,
            Status = "queued",
            Resource = resource,
            Format = format,
            ReportType = reportType,
            RequestedAt = requestedAt,
            Message = "Exportacao enfileirada para processamento assincrono"
        };
    }

    private static string NormalizeRequired(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string?> NormalizeFilters(Dictionary<string, string?>? filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in filters)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || !result.TryAdd(item.Key.Trim(), string.IsNullOrWhiteSpace(item.Value) ? null : item.Value.Trim()))
                throw new InvalidOperationException("Filtros de exportacao invalidos ou duplicados.");
        }
        return result;
    }
}

public partial class RequestFileExportCommand : IRequest<RequestFileExportResponse>
{
}
