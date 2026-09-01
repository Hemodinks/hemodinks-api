using HemodinksAPI.Application.Features.Sessions;

namespace HemodinksAPI.Api;

public sealed class AuthenticationSessionCookie
{
    private readonly AuthenticationSessionOptions _options;
    private readonly IWebHostEnvironment _environment;

    public AuthenticationSessionCookie(
        AuthenticationSessionOptions options,
        IWebHostEnvironment environment)
    {
        _options = options;
        _environment = environment;
    }

    public string? Read(HttpContext context)
    {
        return context.Request.Cookies.TryGetValue(_options.RefreshCookieName, out var token)
            ? token
            : null;
    }

    public void Write(HttpContext context, IssuedAuthenticationSession session)
    {
        context.Response.Cookies.Append(
            _options.RefreshCookieName,
            session.RefreshToken,
            CreateOptions(session.RefreshCookieExpiresAt));
    }

    public void Delete(HttpContext context)
    {
        context.Response.Cookies.Delete(_options.RefreshCookieName, CreateOptions(null));
    }

    private CookieOptions CreateOptions(DateTime? expiresAt)
    {
        var secure = !_environment.IsDevelopment() && !_environment.IsEnvironment("Testing");
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = secure ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/api/session",
            IsEssential = true,
            Expires = expiresAt.HasValue ? new DateTimeOffset(expiresAt.Value, TimeSpan.Zero) : null
        };
    }
}
