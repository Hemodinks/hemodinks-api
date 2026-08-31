using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Formatting.Json;

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
    .AddProxyForwarding(builder.Configuration)
    .AddTenancy()
    .AddAuth(builder.Configuration, builder.Environment)
    .AddFrontendCors(builder.Configuration, builder.Environment)
    .AddApiRateLimiting()
    .AddLicensing(builder.Configuration)
    .AddStorage(builder.Configuration, builder.Environment)
    .AddApplicationServices(builder.Configuration, builder.Environment)
    .AddApiDocumentation();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionHandlingMiddleware>();
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
        if (httpContext.Request.Path.StartsWithSegments("/healthz")
            || httpContext.Request.Path.StartsWithSegments("/readyz")
            || httpContext.Request.Path.StartsWithSegments("/livez"))
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
if (app.Environment.IsProduction())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        if (context.User.Identity?.IsAuthenticated == true
            && IsPrivateFileRequest(context.Request.Path))
        {
            context.Response.Headers["Cache-Control"] = "no-store, private";
            context.Response.Headers["Pragma"] = "no-cache";
        }

        return Task.CompletedTask;
    });

    await next();
});
app.UseMiddleware<AuthenticationSessionMiddleware>();
app.UseMiddleware<ClinicaResolutionMiddleware>();
app.Use(async (context, next) =>
{
    var userId = context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
    var globalUserId = context.User.FindFirstValue(GlobalIdentityClaimTypes.UsuarioGlobalId);
    var clinicId = context.User.FindFirstValue(ClinicaClaimTypes.ClinicaId);

    using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
    using (LogContext.PushProperty("RequestMethod", context.Request.Method))
    using (LogContext.PushProperty("RequestPath", context.Request.Path.Value ?? string.Empty))
    using (LogContext.PushProperty("UserId", userId ?? string.Empty))
    using (LogContext.PushProperty("GlobalUserId", globalUserId ?? string.Empty))
    using (LogContext.PushProperty("ClinicId", clinicId ?? string.Empty))
    {
        await next();
    }
});
app.UseMiddleware<ClinicaModuleAccessMiddleware>();
app.UseAuthorization();
app.UseMiddleware<EquipeMutationAuditMiddleware>();

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();

public partial class Program
{
    private static bool IsPrivateFileRequest(PathString path)
    {
        return path.Value?.Contains("/download", StringComparison.OrdinalIgnoreCase) == true
            || path.Value?.Contains("/comprovante", StringComparison.OrdinalIgnoreCase) == true
            || path.Value?.Contains("/foto-perfil", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static void ConfigureSerilog(LoggerConfiguration loggerConfiguration, string contentRootPath)
    {
        var logDirectory = Path.Combine(contentRootPath, "logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, "hemodinks-api-.txt");
        var errorLogFilePath = Path.Combine(logDirectory, MonitoringLogReader.ErrorFilePattern);

        loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Logger(errorLogger => errorLogger
                .MinimumLevel.Error()
                .WriteTo.File(
                    new JsonFormatter(renderMessage: true),
                    errorLogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    shared: true))
            .Enrich.FromLogContext()
            .Enrich.WithThreadId();
    }
}
