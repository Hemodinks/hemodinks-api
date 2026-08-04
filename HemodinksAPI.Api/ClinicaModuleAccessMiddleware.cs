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
            || context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var clinicaId = clinicaContext.GetRequiredClinicaId();
        var subscription = await dbContext.Clinicas
            .AsNoTracking()
            .Where(item => item.Id == clinicaId)
            .Select(item => new
            {
                item.Ativa,
                item.Plano,
                item.ModulosLiberados,
                item.AssinaturaStatus,
                item.TrialAte,
                item.AssinaturaValidaAte
            })
            .SingleAsync(context.RequestAborted);
        var now = DateTime.UtcNow;
        var subscriptionActive = subscription.Ativa
            && (subscription.AssinaturaStatus == ClinicaAssinaturaStatus.Ativa
                && (!subscription.AssinaturaValidaAte.HasValue || subscription.AssinaturaValidaAte >= now)
                || subscription.AssinaturaStatus == ClinicaAssinaturaStatus.Trial
                && (!subscription.TrialAte.HasValue || subscription.TrialAte >= now));

        if (!subscriptionActive)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { message = "Assinatura da clinica inativa ou expirada." },
                context.RequestAborted);
            return;
        }

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
        if (path.StartsWithSegments("/api/atendimentos-cirurgicos")
            || path.StartsWithSegments("/api/faturamentos")
            || path.StartsWithSegments("/api/financeiro")
            || path.StartsWithSegments("/api/convenios-procedimentos-precos"))
        {
            return ClinicaModulos.Faturamento;
        }

        if (path.StartsWithSegments("/api/pacientes", out var patientPath)
            && patientPath.Value?.EndsWith("/resumo-financeiro", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ClinicaModulos.Faturamento;
        }

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
