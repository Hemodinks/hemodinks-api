using System.Diagnostics;
using HemodinksAPI.Api;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddNonProductionUserSecretsFallback(builder.Environment);

builder.Host.UseSerilog(
    (_, _, loggerConfiguration) => Program.ConfigureSerilog(loggerConfiguration),
    preserveStaticLogger: false,
    writeToProviders: true);

builder.Services
    .AddDatabase(builder.Configuration)
    .AddAuth(builder.Configuration, builder.Environment)
    .AddFrontendCors(builder.Configuration)
    .AddApiRateLimiting()
    .AddLicensing(builder.Configuration)
    .AddStorage(builder.Configuration, builder.Environment)
    .AddApplicationServices(builder.Configuration, builder.Environment)
    .AddApiDocumentation();

builder.AddOpenTelemetryObservability();

var app = builder.Build();
var otlpEndpointConfigured = !string.IsNullOrWhiteSpace(app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
app.Logger.LogInformation(
    "OpenTelemetry observability initialized. OTLP exporter configured: {OtlpEndpointConfigured}",
    otlpEndpointConfigured);

await app.InitializeDatabaseAsync();

if (!app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["AzureStorage:ConnectionString"]))
{
    var localStorageRootPath = LocalStoragePathHelper.ResolveRootPath(
        app.Configuration["LocalStorage:RootPath"],
        app.Environment.ContentRootPath);
    var localStorageRequestPath = LocalStoragePathHelper.NormalizeRequestPath(app.Configuration["LocalStorage:RequestPath"]);

    Directory.CreateDirectory(localStorageRootPath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(localStorageRootPath),
        RequestPath = localStorageRequestPath
    });
}

app.UseApiDocumentation();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
        return Task.CompletedTask;
    });

    await next();
});
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms [request_id: {TraceIdentifier}, trace_id: {TraceId}, span_id: {SpanId}]";
    options.GetLevel = (httpContext, _, exception) =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/healthz"))
        {
            return LogEventLevel.Verbose;
        }

        return exception != null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var activity = Activity.Current;

        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("TraceIdentifier", httpContext.TraceIdentifier);

        if (activity is not null)
        {
            diagnosticContext.Set("TraceId", activity.TraceId.ToString());
            diagnosticContext.Set("SpanId", activity.SpanId.ToString());
        }
    };
});
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapApiEndpoints();

app.Run();

public partial class Program
{
    public static void ConfigureSerilog(LoggerConfiguration loggerConfiguration)
    {
        loggerConfiguration
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/hemodinks-api-.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithThreadId();
    }
}
