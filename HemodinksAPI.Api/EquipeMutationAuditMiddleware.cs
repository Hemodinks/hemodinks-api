using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

public sealed class EquipeMutationAuditMiddleware
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;

    public EquipeMutationAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        PlatformAuditService auditService,
        ILogger<EquipeMutationAuditMiddleware> logger)
    {
        await _next(httpContext);

        if (!MutatingMethods.Contains(httpContext.Request.Method)
            || httpContext.User.FindFirst(GlobalIdentityClaimTypes.EquipeId) == null
            || httpContext.Request.Path.StartsWithSegments("/api/equipe-auth/pin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var clinicId = int.TryParse(
                httpContext.User.FindFirstValue(ClinicaClaimTypes.ClinicaId),
                out var parsedClinicId)
                ? parsedClinicId
                : (int?)null;
            var success = httpContext.Response.StatusCode < StatusCodes.Status400BadRequest;
            await auditService.RecordAsync(
                httpContext,
                $"team.http.{httpContext.Request.Method.ToLowerInvariant()}",
                httpContext.Request.Path.Value ?? "api",
                null,
                clinicId,
                new { httpContext.Response.StatusCode },
                success,
                httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao registrar auditoria da operacao realizada pela equipe");
        }
    }
}
