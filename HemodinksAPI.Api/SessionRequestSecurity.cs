namespace HemodinksAPI.Api;

internal static class SessionRequestSecurity
{
    internal static bool IsTrusted(HttpContext context, IConfiguration configuration, bool login = false)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (login && string.IsNullOrEmpty(origin))
            return context.Request.Headers["Sec-Fetch-Site"] != "cross-site";
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return context.Request.Headers["X-Session-Refresh"] == "1"
            && (string.IsNullOrEmpty(origin) || origins.Any(item =>
                string.Equals(item.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)));
    }
}
