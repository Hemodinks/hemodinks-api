using System.Net;

namespace HemodinksAPI.Api;

// Only for the verified ACA HTTP ingress boundary: ACA appends the rightmost IP
// and overwrites proto. Client-supplied prefixes never determine identity.
public sealed class AzureContainerAppsIngressMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        var proto = context.Request.Headers["X-Forwarded-Proto"].ToString();
        if (forwarded.Length > 0)
        {
            var last = forwarded.AsSpan(forwarded.LastIndexOf(',') + 1).Trim();
            if (forwarded.Length <= 8192 && (proto is "http" or "https")
                && IPAddress.TryParse(last, out var address) && !last.Contains('%'))
            {
                context.Connection.RemoteIpAddress = address;
                context.Request.Scheme = proto;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            }
        }
        return next(context);
    }
}
