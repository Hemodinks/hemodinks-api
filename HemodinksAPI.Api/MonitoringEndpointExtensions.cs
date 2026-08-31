using System.Security.Claims;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

public static class MonitoringEndpointExtensions
{
    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        app.MapGet("/api/monitoramento/erros", GetErrors)
            .WithTags("Monitoramento")
            .WithSummary("Listar erros técnicos")
            .WithDescription("Retorna somente eventos de erro. Administradores visualizam a própria clínica; o SuperAdministrador visualiza todas.")
            .RequireAuthorization("Administrador");

        app.MapDelete("/api/monitoramento/erros", ClearErrors)
            .WithTags("Monitoramento")
            .WithSummary("Limpar erros técnicos")
            .WithDescription("Oculta os erros existentes no escopo do administrador sem interromper a gravação de novos eventos.")
            .RequireAuthorization("Administrador");
    }

    private static IResult GetErrors(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        int page = 1,
        int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var isSuperAdministrator = httpContext.User.FindFirstValue("perfilId") == Perfil.SuperAdministradorId.ToString();
        int? clinicId = null;
        if (!isSuperAdministrator)
        {
            if (!int.TryParse(httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var parsedClinicId))
            {
                return Results.Forbid();
            }

            clinicId = parsedClinicId;
        }

        var reader = new MonitoringLogReader(Path.Combine(environment.ContentRootPath, "logs"));
        return Results.Ok(reader.Read(page, pageSize, clinicId));
    }

    private static async Task<IResult> ClearErrors(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var isSuperAdministrator = httpContext.User.FindFirstValue("perfilId") == Perfil.SuperAdministradorId.ToString();
        int? clinicId = null;
        if (!isSuperAdministrator)
        {
            if (!int.TryParse(httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId), out var parsedClinicId))
            {
                return Results.Forbid();
            }

            clinicId = parsedClinicId;
        }

        var reader = new MonitoringLogReader(Path.Combine(environment.ContentRootPath, "logs"));
        var clearedAt = await reader.ClearAsync(clinicId, cancellationToken);
        return Results.Ok(new MonitoringClearResult(clearedAt));
    }
}
