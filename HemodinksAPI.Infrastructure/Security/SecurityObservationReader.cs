using System.Text.Json;
using HemodinksAPI.Application.Security;

namespace HemodinksAPI.Infrastructure.Security;

// Same local rolling Serilog storage as technical monitoring. No analysis or I/O in login.
public sealed class SecurityObservationReader(IHostEnvironment environment) : ISecurityObservationReader
{
    public SecurityObservationPage Read(int page, int pageSize, int? clinicId)
    {
        page = Math.Clamp(page, 1, 100);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var directory = Path.Combine(environment.ContentRootPath, "logs");
        var items = new Queue<SecurityObservation>();
        try
        {
            if (!Directory.Exists(directory)) return new(items.ToArray(), page, pageSize);
            foreach (var path in Directory.EnumerateFiles(directory, "hemodinks-security-*.json").Order().TakeLast(SecurityObservationWorker.MaximumFiles))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var value = Parse(line);
                    if (value != null && (!clinicId.HasValue || value.ClinicId == clinicId)
                        && value.Timestamp >= DateTimeOffset.UtcNow.AddDays(-SecurityObservationWorker.RetentionDays))
                    {
                        if (items.Count >= 10000) items.Dequeue();
                        items.Enqueue(value);
                    }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new(items.OrderByDescending(x => x.Timestamp).Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize);
    }

    internal static SecurityObservation? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var properties = document.RootElement.GetProperty("Properties");
            string? Get(string key) => properties.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() : null;
            string Safe(string? value, int length = 100) => new((value ?? "").Take(length)
                .Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':').ToArray());
            if (!Enum.TryParse<SecurityEventKind>(Get("SecurityEvent"), out var kind) || !Enum.IsDefined(kind)) return null;
            if (!DateTimeOffset.TryParse(Get("ObservedAt"), out var timestamp)) return null;
            int? clinic = properties.TryGetProperty("ClinicId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && number > 0 ? number : null;
            var key = Get("AccountKey");
            if (key?.Length != 64 || !key.All(char.IsAsciiHexDigit)) key = null;
            return new(timestamp, kind, Safe(Get("Operation")), Safe(Get("Reason")), Safe(Get("RequestId")), clinic, key);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
}
