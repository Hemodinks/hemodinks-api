using System.Diagnostics;
using System.Text.Json.Serialization;
using HemodinksAPI.Api;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Configuration.AddNonProductionUserSecretsFallback(builder.Environment);
builder.AddServiceDefaults();

builder.Host.UseSerilog(
    (_, _, loggerConfiguration) => Program.ConfigureSerilog(loggerConfiguration, builder.Environment.ContentRootPath),
    preserveStaticLogger: false,
    writeToProviders: true);

builder.Services
    .AddDatabase(builder.Configuration)
    .AddTenancy()
    .AddAuth(builder.Configuration, builder.Environment)
    .AddFrontendCors(builder.Configuration)
    .AddApiRateLimiting()
    .AddLicensing(builder.Configuration)
    .AddStorage(builder.Configuration, builder.Environment)
    .AddApplicationServices(builder.Configuration, builder.Environment)
    .AddApiDocumentation();

var app = builder.Build();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException exception)
    {
        app.Logger.LogWarning(exception, "Invalid request body for {Path}", context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Alguns campos estao ausentes ou possuem formato invalido. Revise os dados informados."
        });
    }
});
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    if (response.StatusCode == StatusCodes.Status400BadRequest)
    {
        await response.WriteAsJsonAsync(new
        {
            message = "Alguns campos estao ausentes ou possuem formato invalido. Revise os dados informados."
        });
    }
});
var newRelicProfilingEnabled = string.Equals(app.Configuration["CORECLR_ENABLE_PROFILING"], "1", StringComparison.Ordinal);
var newRelicAppNameConfigured = !string.IsNullOrWhiteSpace(app.Configuration["NEW_RELIC_APP_NAME"]);
app.Logger.LogInformation(
    "New Relic profiler requested: {NewRelicProfilingEnabled}. App name configured: {NewRelicAppNameConfigured}",
    newRelicProfilingEnabled,
    newRelicAppNameConfigured);

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
app.UseMiddleware<ClinicaResolutionMiddleware>();
app.UseMiddleware<ClinicaModuleAccessMiddleware>();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();

public partial class Program
{
    public static void ConfigureSerilog(LoggerConfiguration loggerConfiguration, string contentRootPath)
    {
        var logDirectory = Path.Combine(contentRootPath, "logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, "hemodinks-api-.txt");

        loggerConfiguration
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(logFilePath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithThreadId();
    }
}
