using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Application.Features.Exports.Commands;

public partial class RequestFileExportCommand
{
    public CurrentUserContext? CurrentUser { get; set; }

    public string Resource { get; set; } = null!;

    public string Format { get; set; } = null!;

    public Dictionary<string, string?>? Filters { get; set; }
}

public class RequestFileExportResponse
{
    public Guid JobId { get; set; }

    public string Status { get; set; } = null!;

    public string Resource { get; set; } = null!;

    public string Format { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public string Message { get; set; } = null!;
}
