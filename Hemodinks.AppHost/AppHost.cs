using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var frontPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "hemodinks-front"));
var apiMode = GetOptionalConfiguration(builder.Configuration, "HEMODINKS_API_MODE", "project");

if (string.Equals(apiMode, "container", StringComparison.OrdinalIgnoreCase))
{
    ConfigureFront(builder, frontPath, AddContainerizedApi(builder));
}
else
{
    ConfigureFront(builder, frontPath, AddLocalApiProject(builder));
}

builder.Build().Run();

IResourceBuilder<ProjectResource> AddLocalApiProject(IDistributedApplicationBuilder builder)
{
    return builder.AddProject<Projects.HemodinksAPI_Api>("api")
        .WithExternalHttpEndpoints()
        .WithHttpHealthCheck("/healthz");
}

IResourceBuilder<ContainerResource> AddContainerizedApi(IDistributedApplicationBuilder builder)
{
    var appSettings = new ContainerizedApiSettings(
        AspNetCoreEnvironment: GetOptionalConfiguration(builder.Configuration, "ASPNETCORE_ENVIRONMENT", "Development"),
        DatabaseName: GetOptionalConfiguration(builder.Configuration, "MSSQL_DATABASE", "HemodinksDB"),
        SqlServerPassword: GetRequiredConfiguration(
            builder.Configuration,
            "Defina MSSQL_SA_PASSWORD no ambiente ou nos User Secrets do Hemodinks.AppHost para usar o modo container.",
            "MSSQL_SA_PASSWORD"),
        JwtSecretKey: GetRequiredConfiguration(
            builder.Configuration,
            "Defina JWT_SECRET_KEY no ambiente ou nos User Secrets do Hemodinks.AppHost para usar o modo container.",
            "JWT_SECRET_KEY"),
        JwtIssuer: GetOptionalConfiguration(builder.Configuration, "JWT_ISSUER", "HemodinksAPI"),
        JwtAudience: GetOptionalConfiguration(builder.Configuration, "JWT_AUDIENCE", "HemodinksAPI"),
        JwtExpirationMinutes: GetOptionalConfiguration(builder.Configuration, "JWT_EXPIRATION_MINUTES", "30"),
        AsyncQueuesEnabled: GetOptionalConfiguration(builder.Configuration, "AsyncQueues__Enabled", "false"),
        AsyncQueuesPasswordResetEnabled: GetOptionalConfiguration(
            builder.Configuration,
            "AsyncQueues__PasswordResetEnabled",
            "false"),
        AsyncQueuesFileExportEnabled: GetOptionalConfiguration(
            builder.Configuration,
            "AsyncQueues__FileExportEnabled",
            "false"),
        AsyncQueuesPasswordResetEmailQueueName: GetOptionalConfiguration(
            builder.Configuration,
            "AsyncQueues__PasswordResetEmailQueueName",
            "password-reset-emails"),
        AsyncQueuesFileExportQueueName: GetOptionalConfiguration(
            builder.Configuration,
            "AsyncQueues__FileExportQueueName",
            "file-export-jobs"),
        AzureStorageContainerName: GetOptionalConfiguration(builder.Configuration, "AZURE_STORAGE_CONTAINER_NAME", "profile-photos"),
        AzureStoragePublicBaseUrl: GetOptionalConfiguration(builder.Configuration, "AZURE_STORAGE_PUBLIC_BASE_URL", string.Empty),
        AzureStorageMaxBytes: GetOptionalConfiguration(builder.Configuration, "AZURE_STORAGE_MAX_BYTES", "2097152"),
        AzureStoragePatientFilesContainerName: GetOptionalConfiguration(
            builder.Configuration,
            "AZURE_STORAGE_PATIENT_FILES_CONTAINER_NAME",
            "patient-files"),
        AzureStoragePatientFilesPublicBaseUrl: GetOptionalConfiguration(
            builder.Configuration,
            "AZURE_STORAGE_PATIENT_FILES_PUBLIC_BASE_URL",
            string.Empty),
        AzureStoragePatientFileMaxBytes: GetOptionalConfiguration(
            builder.Configuration,
            "AZURE_STORAGE_PATIENT_FILE_MAX_BYTES",
            "10485760"),
        EmailProvider: GetOptionalConfiguration(builder.Configuration, "Email__Provider", "GmailSmtp"),
        EmailSmtpHost: GetOptionalConfiguration(builder.Configuration, "Email__Smtp__Host", "smtp.gmail.com"),
        EmailSmtpPort: GetOptionalConfiguration(builder.Configuration, "Email__Smtp__Port", "587"),
        EmailSmtpUsername: GetOptionalConfiguration(
            builder.Configuration,
            "Email__Smtp__Username",
            "hemodinks.gestao.saude@gmail.com"),
        EmailSmtpPassword: GetOptionalConfiguration(builder.Configuration, "Email__Smtp__Password", string.Empty),
        EmailFromEmail: GetOptionalConfiguration(
            builder.Configuration,
            "Email__FromEmail",
            "hemodinks.gestao.saude@gmail.com"),
        EmailFromName: GetOptionalConfiguration(builder.Configuration, "Email__FromName", "Hemodinks"),
        EmailBrandLogoUrl: GetOptionalConfiguration(builder.Configuration, "Email__BrandLogoUrl", string.Empty),
        FrontendResetPasswordUrl: GetOptionalConfiguration(
            builder.Configuration,
            "Frontend__ResetPasswordUrl",
            "http://localhost:5173/reset-password"));

    var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));
    var logsPath = Path.GetFullPath(Path.Combine(repositoryRoot, "logs"));
    Directory.CreateDirectory(logsPath);

    const string azuriteConnectionString = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://azurite";

    var sqlServer = builder.AddContainer("sqlserver", "mcr.microsoft.com/mssql/server", "2022-latest")
        .WithContainerName("sqlserver")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithEnvironment("MSSQL_SA_PASSWORD", appSettings.SqlServerPassword)
        .WithEnvironment("MSSQL_PID", "Developer")
        .WithEndpoint(targetPort: 1433, port: 14330, name: "tcp", isProxied: false)
        .WithVolume("hemodinks-apphost-sqlserver-data", "/var/opt/mssql")
        .WithContainerRuntimeArgs(
            "--health-cmd",
            "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $MSSQL_SA_PASSWORD -Q \"SELECT 1\" || exit 1",
            "--health-interval",
            "10s",
            "--health-timeout",
            "10s",
            "--health-retries",
            "10",
            "--health-start-period",
            "20s");

    var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite", "latest")
        .WithContainerName("azurite")
        .WithArgs(
            "azurite",
            "--blobHost",
            "0.0.0.0",
            "--queueHost",
            "0.0.0.0",
            "--tableHost",
            "0.0.0.0",
            "--location",
            "/data",
            "--skipApiVersionCheck")
        .WithEndpoint(targetPort: 10000, port: 10000, name: "blob", isProxied: false)
        .WithEndpoint(targetPort: 10001, port: 10001, name: "queue", isProxied: false)
        .WithEndpoint(targetPort: 10002, port: 10002, name: "table", isProxied: false)
        .WithVolume("hemodinks-apphost-azurite-data", "/data");

    var api = builder.AddDockerfile("api", repositoryRoot)
        .WithContainerName("hemodinks-api-container")
        .WithHttpEndpoint(targetPort: 8080, port: 5000, isProxied: false)
        .WithHttpHealthCheck("/healthz")
        .WithBindMount(logsPath, "/app/logs")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", appSettings.AspNetCoreEnvironment)
        .WithEnvironment("ConnectionStrings__DefaultConnection", appSettings.SqlServerConnectionString)
        .WithEnvironment("JwtSettings__SecretKey", appSettings.JwtSecretKey)
        .WithEnvironment("JwtSettings__Issuer", appSettings.JwtIssuer)
        .WithEnvironment("JwtSettings__Audience", appSettings.JwtAudience)
        .WithEnvironment("JwtSettings__ExpirationMinutes", appSettings.JwtExpirationMinutes)
        .WithEnvironment("AsyncQueues__Enabled", appSettings.AsyncQueuesEnabled)
        .WithEnvironment("AsyncQueues__PasswordResetEnabled", appSettings.AsyncQueuesPasswordResetEnabled)
        .WithEnvironment("AsyncQueues__FileExportEnabled", appSettings.AsyncQueuesFileExportEnabled)
        .WithEnvironment("AsyncQueues__ConnectionString", azuriteConnectionString)
        .WithEnvironment(
            "AsyncQueues__PasswordResetEmailQueueName",
            appSettings.AsyncQueuesPasswordResetEmailQueueName)
        .WithEnvironment("AsyncQueues__FileExportQueueName", appSettings.AsyncQueuesFileExportQueueName)
        .WithEnvironment("AzureStorage__ConnectionString", azuriteConnectionString)
        .WithEnvironment("AzureStorage__ContainerName", appSettings.AzureStorageContainerName)
        .WithEnvironment("AzureStorage__PublicBaseUrl", appSettings.AzureStoragePublicBaseUrl)
        .WithEnvironment("AzureStorage__MaxBytes", appSettings.AzureStorageMaxBytes)
        .WithEnvironment(
            "AzureStorage__PatientFilesContainerName",
            appSettings.AzureStoragePatientFilesContainerName)
        .WithEnvironment(
            "AzureStorage__PatientFilesPublicBaseUrl",
            appSettings.AzureStoragePatientFilesPublicBaseUrl)
        .WithEnvironment("AzureStorage__PatientFileMaxBytes", appSettings.AzureStoragePatientFileMaxBytes)
        .WithEnvironment("Email__Provider", appSettings.EmailProvider)
        .WithEnvironment("Email__Smtp__Host", appSettings.EmailSmtpHost)
        .WithEnvironment("Email__Smtp__Port", appSettings.EmailSmtpPort)
        .WithEnvironment("Email__Smtp__Username", appSettings.EmailSmtpUsername)
        .WithEnvironment("Email__Smtp__Password", appSettings.EmailSmtpPassword)
        .WithEnvironment("Email__FromEmail", appSettings.EmailFromEmail)
        .WithEnvironment("Email__FromName", appSettings.EmailFromName)
        .WithEnvironment("Email__BrandLogoUrl", appSettings.EmailBrandLogoUrl)
        .WithEnvironment("Frontend__ResetPasswordUrl", appSettings.FrontendResetPasswordUrl)
        .WaitFor(sqlServer)
        .WaitForStart(azurite);

    ConfigureOptionalEnvironment(api, builder.Configuration, "CORECLR_ENABLE_PROFILING");
    ConfigureOptionalEnvironment(api, builder.Configuration, "NEW_RELIC_LICENSE_KEY");
    ConfigureOptionalEnvironment(api, builder.Configuration, "NEW_RELIC_APP_NAME");
    ConfigureOptionalEnvironment(api, builder.Configuration, "OTEL_EXPORTER_OTLP_EXTERNAL_ENDPOINT");
    ConfigureOptionalEnvironment(api, builder.Configuration, "OTEL_EXPORTER_OTLP_EXTERNAL_PROTOCOL");
    ConfigureOptionalEnvironment(api, builder.Configuration, "OTEL_EXPORTER_OTLP_EXTERNAL_HEADERS");

    return api;
}

void ConfigureFront<TResource>(
    IDistributedApplicationBuilder builder,
    string frontPath,
    IResourceBuilder<TResource> api)
    where TResource : IResourceWithEndpoints
{
    var front = builder.AddJavaScriptApp("front", frontPath, "dev")
        .WithHttpEndpoint(targetPort: 5173, port: 5173, isProxied: false)
        .WithEnvironment("VITE_API_URL", api.GetEndpoint("http"));

    ConfigureOptionalEnvironment(front, builder.Configuration, "ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL");
    ConfigureOptionalEnvironment(front, builder.Configuration, "VITE_OTEL_EXPORTER_OTLP_ENDPOINT");
    ConfigureOptionalEnvironment(front, builder.Configuration, "VITE_OTEL_EXPORTER_OTLP_TRACES_ENDPOINT");
    ConfigureOptionalEnvironment(front, builder.Configuration, "VITE_OTEL_EXPORTER_OTLP_HEADERS");
    ConfigureOptionalEnvironment(front, builder.Configuration, "VITE_OTEL_EXPORTER_OTLP_TRACES_HEADERS");
}

void ConfigureOptionalEnvironment<TResource>(
    IResourceBuilder<TResource> resource,
    IConfiguration configuration,
    string key)
    where TResource : IResourceWithEnvironment
{
    var value = configuration[key];

    if (!string.IsNullOrWhiteSpace(value))
    {
        resource.WithEnvironment(key, value);
    }
}

string GetOptionalConfiguration(IConfiguration configuration, string key, string defaultValue)
{
    return string.IsNullOrWhiteSpace(configuration[key]) ? defaultValue : configuration[key]!;
}

string GetRequiredConfiguration(IConfiguration configuration, string errorMessage, params string[] keys)
{
    foreach (var key in keys)
    {
        if (!string.IsNullOrWhiteSpace(configuration[key]))
        {
            return configuration[key]!;
        }
    }

    throw new InvalidOperationException(errorMessage);
}

sealed record ContainerizedApiSettings(
    string AspNetCoreEnvironment,
    string DatabaseName,
    string SqlServerPassword,
    string JwtSecretKey,
    string JwtIssuer,
    string JwtAudience,
    string JwtExpirationMinutes,
    string AsyncQueuesEnabled,
    string AsyncQueuesPasswordResetEnabled,
    string AsyncQueuesFileExportEnabled,
    string AsyncQueuesPasswordResetEmailQueueName,
    string AsyncQueuesFileExportQueueName,
    string AzureStorageContainerName,
    string AzureStoragePublicBaseUrl,
    string AzureStorageMaxBytes,
    string AzureStoragePatientFilesContainerName,
    string AzureStoragePatientFilesPublicBaseUrl,
    string AzureStoragePatientFileMaxBytes,
    string EmailProvider,
    string EmailSmtpHost,
    string EmailSmtpPort,
    string EmailSmtpUsername,
    string EmailSmtpPassword,
    string EmailFromEmail,
    string EmailFromName,
    string EmailBrandLogoUrl,
    string FrontendResetPasswordUrl)
{
    public string SqlServerConnectionString =>
        string.Join(
            ';',
            $"Server=sqlserver",
            $"Database={DatabaseName}",
            "User Id=sa",
            $"Pwd={SqlServerPassword}",
            "TrustServerCertificate=true",
            "Encrypt=false");
}
