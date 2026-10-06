using System.Security.Claims;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Features.Sessions;
using Microsoft.AspNetCore.Http;

namespace HemodinksAPI.Tests;

public sealed class ValidatedSessionRequestTests
{
    private static (DefaultHttpContext Http, SessionValidationSnapshot Snapshot) Create()
    {
        var snapshot = new SessionValidationSnapshot(Guid.NewGuid(), 11, 12, 13, 14, "Clinic", "clinic",
            Guid.NewGuid(), false, 15, 16, 17, 18, true, 2);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("sid", snapshot.SessionId.ToString()), new Claim("security_version", snapshot.SecurityVersion.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "13"), new Claim("usuarioGlobalId", "11"), new Claim("usuarioClinicaId", "12"),
            new Claim("clinicaId", "14"), new Claim("perfilId", "2"), new Claim("equipeId", "15"), new Claim("equipeVersaoSessao", "16"),
            new Claim("equipeOperadorId", "17"), new Claim("operadorVersaoSessao", "18"), new Claim("identificacaoConfiavel", "true")
        }, "test")) };
        ValidatedSessionRequest.Set(http, snapshot);
        return (http, snapshot);
    }

    [Theory]
    [InlineData("sid")]
    [InlineData("security_version")]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("usuarioGlobalId")]
    [InlineData("usuarioClinicaId")]
    [InlineData("clinicaId")]
    [InlineData("perfilId")]
    [InlineData("equipeId")]
    [InlineData("equipeVersaoSessao")]
    [InlineData("equipeOperadorId")]
    [InlineData("operadorVersaoSessao")]
    [InlineData("identificacaoConfiavel")]
    public void ContextChangeCannotReuseSnapshot(string type)
    {
        var (http, snapshot) = Create();
        Assert.Same(snapshot, ValidatedSessionRequest.Get(http));
        var identity = (ClaimsIdentity)http.User.Identity!;
        identity.RemoveClaim(identity.FindFirst(type));
        Assert.Null(ValidatedSessionRequest.Get(http));
        identity.AddClaim(new Claim(type, "999"));
        Assert.Null(ValidatedSessionRequest.Get(http));
    }

    [Fact]
    public void SnapshotIsConfinedToItsAuthenticatedRequest()
    {
        var (http, snapshot) = Create();
        Assert.Same(snapshot, ValidatedSessionRequest.Get(http));
        Assert.Null(ValidatedSessionRequest.Get(new DefaultHttpContext { User = http.User }));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(http.User.Claims));
        Assert.Null(ValidatedSessionRequest.Get(http));
    }
}
