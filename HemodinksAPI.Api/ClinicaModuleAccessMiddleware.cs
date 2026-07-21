using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class ClinicaModuleAccessMiddleware
{
    private readonly RequestDelegate _next;

    public ClinicaModuleAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ClinicaContext clinicaContext,
        AppDbContext dbContext)
    {
        var requiredModule = ResolveRequiredModule(context.Request.Path);
        if (requiredModule == null
            || context.User.Identity?.IsAuthenticated != true
            || context.User.IsInRole("SuperAdministrador")
            || context.User.HasClaim("perfilId", Perfil.SuperAdministradorId.ToString()))
        {
            await _next(context);
            return;
        }

        var clinicaId = clinicaContext.GetRequiredClinicaId();
        var subscription = await dbContext.Clinicas
            .AsNoTracking()
            .Where(item => item.Id == clinicaId)
            .Select(item => new { item.Plano, item.ModulosLiberados })
            .SingleAsync(context.RequestAborted);
        var allowed = ClinicaModulos.GetEffective(subscription.Plano, subscription.ModulosLiberados)
            .Contains(requiredModule, StringComparer.OrdinalIgnoreCase);

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { message = "Modulo nao contratado no plano da clinica." },
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static string? ResolveRequiredModule(PathString path)
    {
        if (path.StartsWithSegments("/api/users")) return ClinicaModulos.Usuarios;
        if (path.StartsWithSegments("/api/pacientes")
            || path.StartsWithSegments("/api/cbhpm")
            || path.StartsWithSegments("/api/convenios")
            || path.StartsWithSegments("/api/hospitais")
            || path.StartsWithSegments("/api/opme")) return ClinicaModulos.Pacientes;
        if (path.StartsWithSegments("/api/faturamentos-medicos")) return ClinicaModulos.Faturamento;
        if (path.StartsWithSegments("/api/grupos-medicos")) return ClinicaModulos.GruposMedicos;
        if (path.StartsWithSegments("/api/events")) return ClinicaModulos.Agenda;
        return null;
    }
}
