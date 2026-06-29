using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Authorization;
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

    public RequestFileExportCommandHandler(
        IFileExportQueue fileExportQueue,
        ILogger<RequestFileExportCommandHandler> logger)
    {
        _fileExportQueue = fileExportQueue;
        _logger = logger;
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

        var requestedAt = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var message = new FileExportQueueMessage(
            jobId,
            resource,
            format,
            currentUser.Id,
            currentUser.PerfilId,
            requestedAt,
            NormalizeFilters(request.Filters));

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

        return filters
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(
                item => item.Key.Trim(),
                item => string.IsNullOrWhiteSpace(item.Value) ? null : item.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }
}

public partial class RequestFileExportCommand : IRequest<RequestFileExportResponse>
{
}
