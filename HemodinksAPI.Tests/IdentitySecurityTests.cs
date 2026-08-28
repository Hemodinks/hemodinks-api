using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class IdentitySecurityTests
{
    [Fact]
    public void GeneratedJwt_DoesNotExposeCpf_AndKeepsAuthorizationClaims()
    {
        var service = new JwtTokenService(
            new JwtSettings
            {
                SecretKey = "0123456789abcdef0123456789abcdef",
                Issuer = "HemodinksAPI",
                Audience = "HemodinksAPI",
                ExpirationMinutes = 30
            },
            NullLogger<JwtTokenService>.Instance);
        var user = new User
        {
            Id = 10,
            ClinicaId = 20,
            Nome = "Usuario Teste",
            Email = "usuario@example.com",
            Cpf = "52998224725",
            PerfilId = Perfil.MedicosId,
            Perfil = new Perfil { Id = Perfil.MedicosId, Nome = "Medico" },
            Clinica = new Clinica { Id = 20, Slug = "clinica-teste" },
            Senha = "hash"
        };
        var global = new UsuarioGlobal { Id = 30, Nome = user.Nome, Email = user.Email, Senha = "hash" };
        var membership = new UsuarioClinica
        {
            Id = 40,
            UsuarioGlobalId = global.Id,
            ClinicaId = user.ClinicaId,
            UserId = user.Id,
            PerfilId = user.PerfilId
        };

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            service.GenerateToken(global, membership, user, Guid.NewGuid()));

        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "cpf");
        Assert.Equal(user.PerfilId.ToString(), jwt.Claims.Single(claim => claim.Type == "perfilId").Value);
        Assert.Equal(user.ClinicaId.ToString(), jwt.Claims.Single(claim => claim.Type == ClinicaClaimTypes.ClinicaId).Value);
        Assert.Equal(global.Id.ToString(), jwt.Claims.Single(claim => claim.Type == GlobalIdentityClaimTypes.UsuarioGlobalId).Value);
        Assert.Equal(membership.Id.ToString(), jwt.Claims.Single(claim => claim.Type == GlobalIdentityClaimTypes.UsuarioClinicaId).Value);
        Assert.Contains(jwt.Claims, claim => claim.Type == AuthenticationSessionClaimTypes.SessionId);
    }

    [Fact]
    public void CurrentUserContext_RejectsIdentityWithoutStructuralTenantClaims()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim("perfilId", Perfil.MedicosId.ToString()));

        Assert.Null(principal.ToCurrentUserContext());
    }

    [Fact]
    public void CurrentUserContext_AcceptsCompleteIdentity()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim(ClaimTypes.Name, "Usuario"),
            new Claim("perfilId", Perfil.MedicosId.ToString()),
            new Claim(ClinicaClaimTypes.ClinicaId, "20"),
            new Claim(ClinicaClaimTypes.ClinicaSlug, "clinica-teste"),
            new Claim(GlobalIdentityClaimTypes.UsuarioGlobalId, "30"),
            new Claim(GlobalIdentityClaimTypes.UsuarioClinicaId, "40"));

        var currentUser = principal.ToCurrentUserContext();

        Assert.NotNull(currentUser);
        Assert.Equal(20, currentUser.ClinicaId);
        Assert.Equal(30, currentUser.UsuarioGlobalId);
        Assert.Equal(40, currentUser.UsuarioClinicaId);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
