using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string DefaultOtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ExternalOtlpEndpointKey = "OTEL_EXPORTER_OTLP_EXTERNAL_ENDPOINT";
    private const string ExternalOtlpHeadersKey = "OTEL_EXPORTER_OTLP_EXTERNAL_HEADERS";
    private const string ExternalOtlpProtocolKey = "OTEL_EXPORTER_OTLP_EXTERNAL_PROTOCOL";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var exportToConfiguredOtlp = !string.IsNullOrWhiteSpace(builder.Configuration[DefaultOtlpEndpointKey]);
        var externalOtlp = GetExternalOtlpExporterSettings(builder.Configuration);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (exportToConfiguredOtlp)
            {
                logging.AddOtlpExporter();
            }

            if (externalOtlp is not null)
            {
                logging.AddOtlpExporter(options => ConfigureOtlpExporter(options, externalOtlp));
            }
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (exportToConfiguredOtlp)
                {
                    metrics.AddOtlpExporter();
                }

                if (externalOtlp is not null)
                {
                    metrics.AddOtlpExporter(options => ConfigureOtlpExporter(options, externalOtlp));
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();

                if (exportToConfiguredOtlp)
                {
                    tracing.AddOtlpExporter();
                }

                if (externalOtlp is not null)
                {
                    tracing.AddOtlpExporter(options => ConfigureOtlpExporter(options, externalOtlp));
                }
            });

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    private static ExternalOtlpExporterSettings? GetExternalOtlpExporterSettings(IConfiguration configuration)
    {
        var endpointValue = configuration[ExternalOtlpEndpointKey];

        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            return null;
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                $"The OTLP external exporter endpoint '{ExternalOtlpEndpointKey}' must be a valid absolute URI.");
        }

        return new ExternalOtlpExporterSettings(
            endpoint,
            ParseOtlpExportProtocol(configuration[ExternalOtlpProtocolKey]),
            configuration[ExternalOtlpHeadersKey]);
    }

    private static OtlpExportProtocol ParseOtlpExportProtocol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OtlpExportProtocol.Grpc;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
            "http-protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException(
                $"The OTLP external exporter protocol '{ExternalOtlpProtocolKey}' must be 'grpc' or 'http/protobuf'.")
        };
    }

    private static void ConfigureOtlpExporter(OtlpExporterOptions options, ExternalOtlpExporterSettings settings)
    {
        options.Endpoint = settings.Endpoint;
        options.Protocol = settings.Protocol;

        if (!string.IsNullOrWhiteSpace(settings.Headers))
        {
            options.Headers = settings.Headers;
        }
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    private sealed record ExternalOtlpExporterSettings(Uri Endpoint, OtlpExportProtocol Protocol, string? Headers);
}
