using System.Security.Claims;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed class PasswordRecoveryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PlatformDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }
        var principal = context.User;
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var membership = await db.UsuariosClinicas.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.UsuarioGlobal.SecurityVersion, x.UsuarioGlobal.TemporaryPasswordRecovery, x.User.PrecisaTrocarSenha })
            .SingleOrDefaultAsync(context.RequestAborted);
        var version = Guid.TryParse(principal.FindFirstValue("security_version"), out var parsed) ? parsed : Guid.Empty;
        if (membership != null && (membership.SecurityVersion != version
            || (membership.TemporaryPasswordRecovery && principal.FindFirstValue("temporary_password") != "true")))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Sessão expirada. Autentique-se novamente." }, context.RequestAborted);
            return;
        }
        var restricted = membership?.TemporaryPasswordRecovery == true
            || principal.FindFirstValue("temporary_password") == "true";
        var endpointName = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        var allowed = endpointName == "ChangeTemporaryPassword"
            || (endpointName == "ChangePassword" && membership?.TemporaryPasswordRecovery != true)
            || context.Request.Path.Equals("/api/session/sair", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/api/session/renovar", StringComparison.OrdinalIgnoreCase);
        if (restricted && !allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Por segurança, cadastre uma nova senha antes de continuar.", code = "must_change_password" }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
