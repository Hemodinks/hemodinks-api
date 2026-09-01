namespace HemodinksAPI.Api;

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

