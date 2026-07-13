using HemodinksAPI.Application.Tenancy;

namespace HemodinksAPI.Api;

public sealed class ClinicaResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public ClinicaResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        ClinicaContext clinicaContext,
        ClinicaResolutionService clinicaResolutionService,
        ILogger<ClinicaResolutionMiddleware> logger)
    {
        if (!ShouldResolveClinica(httpContext.Request.Path))
        {
            await _next(httpContext);
            return;
        }

        var resolvedClinica = await clinicaResolutionService.ResolveAsync(httpContext, httpContext.RequestAborted);
        if (resolvedClinica == null)
        {
            logger.LogWarning(
                "Clinica nao resolvida para {Method} {Path}. Header {HeaderName} ausente/invalido e nenhum fallback aplicavel.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                ClinicaResolutionService.ClinicaSlugHeaderName);

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "Clinica nao resolvida. Envie X-Clinica-Slug ou use um subdominio configurado."
            }, httpContext.RequestAborted);
            return;
        }

        clinicaContext.SetCurrent(resolvedClinica.Id, resolvedClinica.Slug);
        httpContext.Response.Headers["X-Clinica-Slug"] = resolvedClinica.Slug;

        await _next(httpContext);
    }

    private static bool ShouldResolveClinica(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }
}
