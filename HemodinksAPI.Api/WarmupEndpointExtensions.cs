using System.Diagnostics;
using HemodinksAPI.Application.Data;

namespace HemodinksAPI.Api;

public static class WarmupEndpointExtensions
{
    public static IApplicationBuilder UseInfrastructureWarmup(this IApplicationBuilder app)
    {
        // An exact infrastructure-only branch: never run session/tenant middleware,
        // even if a caller supplies credentials. Other routes keep their pipeline.
        return app.MapWhen(
            context => context.Request.Path.Equals("/api/warmup", StringComparison.OrdinalIgnoreCase),
            branch =>
            {
                branch.UseRouting();
                branch.UseRateLimiter();
                branch.UseEndpoints(endpoints => endpoints.MapGet("/api/warmup", HandleAsync)
                    .AllowAnonymous()
                    .RequireRateLimiting("Warmup")
                    .WithName("InfrastructureWarmup")
                    .Produces(StatusCodes.Status204NoContent)
                    .Produces(StatusCodes.Status503ServiceUnavailable));
            });
    }

    internal static async Task<IResult> HandleAsync(
        HttpContext context,
        IDatabaseReadinessProbe probe,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!configuration.GetValue("Warmup:Enabled", true))
            return Results.NoContent();

        var logger = loggerFactory.CreateLogger("InfrastructureWarmup");
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        logger.LogDebug("WarmupStarted");
        try
        {
            var result = await probe.CheckAsync(validateSchema: false, timeout.Token);
            if (!result.Connected)
            {
                logger.LogWarning("WarmupFailed {WarmupDurationMs} {Reason}", timer.Elapsed.TotalMilliseconds, "Unavailable");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            logger.LogInformation("WarmupCompleted {WarmupDurationMs}", timer.Elapsed.TotalMilliseconds);
            return Results.NoContent();
        }
        catch (Exception exception)
        {
            // Never log the exception/message: SQL errors may contain server details.
            logger.LogWarning("WarmupFailed {WarmupDurationMs} {Reason}",
                timer.Elapsed.TotalMilliseconds,
                exception is OperationCanceledException ? "CancelledOrTimedOut" : "Unavailable");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
