using HemodinksAPI.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace HemodinksAPI.Tests;

public sealed class SessionCookieAttributesTests
{
    [Theory]
    [InlineData("Development", "None", false, "lax")]
    [InlineData("Testing", "Strict", false, "lax")]
    [InlineData("Production", "None", true, "none")]
    [InlineData("Production", "Lax", true, "lax")]
    [InlineData("Staging", "Strict", true, "strict")]
    public void CookieAndDeletionShareRestrictedScope(string environment, string sameSite, bool secure, string expected)
    {
        var cookie = new AuthenticationSessionCookie(new() { RefreshCookieSameSite = sameSite }, new Host { EnvironmentName = environment });
        var context = new DefaultHttpContext();
        cookie.Write(context, new("access", "opaque-refresh", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddHours(12)));
        var header = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", header);
        Assert.Contains("path=/api/session", header);
        Assert.Contains($"samesite={expected}", header);
        Assert.Equal(secure, header.Contains("secure"));
        Assert.DoesNotContain("domain=", header);
        context.Response.Headers.Clear();
        cookie.Delete(context);
        Assert.Contains("path=/api/session", context.Response.Headers.SetCookie.ToString());
        Assert.Contains($"samesite={expected}", context.Response.Headers.SetCookie.ToString());
    }

    private sealed class Host : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
