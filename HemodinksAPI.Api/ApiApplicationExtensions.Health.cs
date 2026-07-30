using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
    private static Task<IResult> ReadinessCheckAsync(
        HealthCheckService healthChecks,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        HealthCheckAsync(healthChecks, configuration, _ => true, cancellationToken);

    private static Task<IResult> LivenessCheckAsync(
        HealthCheckService healthChecks,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        HealthCheckAsync(
            healthChecks,
            configuration,
            registration => registration.Tags.Contains("live"),
            cancellationToken);

    private static async Task<IResult> HealthCheckAsync(
        HealthCheckService healthChecks,
        IConfiguration configuration,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(predicate, cancellationToken);
        var payload = new
        {
            status = report.Status.ToString(),
            checkedAt = DateTimeOffset.UtcNow,
            deployment = new
            {
                commitSha = configuration["Deployment:CommitSha"],
                containerAppName = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"),
                containerAppRevision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
            },
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    data = entry.Value.Data
                })
        };

        return report.Status == HealthStatus.Healthy
            ? Results.Ok(payload)
            : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
