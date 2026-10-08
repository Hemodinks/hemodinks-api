using System.Diagnostics;
using HemodinksAPI.Application.Data;

namespace HemodinksAPI.Api;

public static class WarmupEndpointExtensions
{
    private const int RetryAfterSeconds = 5;
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
                return Unavailable(context, timedOut: false);
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
            return Unavailable(context, exception is OperationCanceledException or TimeoutException);
        }
    }

    private static IResult Unavailable(HttpContext context, bool timedOut)
    {
        context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(new
        {
            code = timedOut ? "warmup_timeout" : "warmup_unavailable",
            requestId = context.TraceIdentifier,
            retryAfterSeconds = RetryAfterSeconds,
            message = "O ambiente ainda nao esta disponivel. Aguarde alguns segundos e tente novamente."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
