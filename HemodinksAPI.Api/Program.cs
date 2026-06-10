using HemodinksAPI.Api;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/hemodinks-api-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithThreadId()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services
    .AddDatabase(builder.Configuration)
    .AddAuth(builder.Configuration)
    .AddFrontendCors(builder.Configuration)
    .AddLicensing(builder.Configuration)
    .AddStorage(builder.Configuration)
    .AddApplicationServices()
    .AddApiDocumentation();

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseApiDocumentation();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapApiEndpoints();

app.Run();

public partial class Program
{
}
