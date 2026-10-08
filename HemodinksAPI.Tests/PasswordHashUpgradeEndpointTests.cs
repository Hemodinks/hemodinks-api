using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class PasswordHashUpgradeEndpointTests
{
    [Theory]
    [InlineData("Pin")]
    [InlineData("Selecao")]
    [InlineData("Nenhuma")]
    public async Task TeamPasswordStage_UpgradesCanonicalCredentialWithoutBypassingOperatorOrChangingPin(string mode)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode == "Pin" ? fixture.Pin : mode == "Selecao" ? fixture.Selection : fixture.Anonymous;
        var oldHash = PasswordHashTestData.Create(TeamLoginFixture.Password, 210_000);
        var pinHash = new PinHasher().HashPin(TeamLoginFixture.PinValue);
        Guid version;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var member = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == team.UserId);
            member.UsuarioGlobal.Senha = oldHash;
            version = member.UsuarioGlobal.SecurityVersion;
            (await db.Users.SingleAsync(x => x.Id == team.UserId)).Senha = oldHash;
            (await db.EquipeOperadores.SingleAsync(x => x.Id == team.OperatorId)).PinHash = pinHash;
            await db.SaveChangesAsync();
        }
        var response = await TeamLoginSecurityTests.LoginAsync(client, team);
        if (mode != "Nenhuma")
        {
            Assert.Null(response.Token);
            Assert.NotNull(response.EquipeDesafio);
            if (mode == "Pin")
                Assert.Equal(HttpStatusCode.Unauthorized, (await TeamLoginSecurityTests.IdentifyAsync(client,
                    response.EquipeDesafio.Token, team.OperatorId, "000000")).StatusCode);
            (await TeamLoginSecurityTests.IdentifyAsync(client, response.EquipeDesafio.Token, team.OperatorId,
                mode == "Pin" ? TeamLoginFixture.PinValue : null)).EnsureSuccessStatusCode();
        }
        else Assert.NotNull(response.Token);

        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var canonical = await verify.UsuariosClinicas.Where(x => x.UserId == team.UserId).Select(x => x.UsuarioGlobal).SingleAsync();
        Assert.StartsWith("PBKDF2-SHA256$600000$", canonical.Senha);
        Assert.Equal(version, canonical.SecurityVersion);
        Assert.Equal(oldHash, (await verify.Users.SingleAsync(x => x.Id == team.UserId)).Senha);
        Assert.Equal(pinHash, (await verify.EquipeOperadores.SingleAsync(x => x.Id == team.OperatorId)).PinHash);
    }

    [Fact]
    public async Task SharedGlobalIdentity_UpgradePreservesBothLocalPasswordsAndOtherClinicMembership()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var weakHash = PasswordHashTestData.Create(TestPasswords.Valid, 210_000);
        var otherHash = PasswordHashTestData.Create("OtherLocalPassword@123", 210_000);
        int localId, otherId, globalId, otherMemberId;
        DateTime? otherUpdatedAt;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var local = await db.Users.SingleAsync(x => x.Email == "gmarcone@gmail.com");
            var member = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == local.Id);
            localId = local.Id;
            globalId = member.UsuarioGlobalId;
            local.Senha = weakHash;
            member.UsuarioGlobal.Senha = weakHash;
            var clinic = new Clinica { Nome = "Other upgrade clinic", Slug = "other-upgrade" };
            var other = new User { Clinica = clinic, Nome = local.Nome, Email = local.Email,
                Telefone = "11987654321", Senha = otherHash, PerfilId = local.PerfilId };
            var otherMember = new UsuarioClinica { Clinica = clinic, User = other, UsuarioGlobal = member.UsuarioGlobal,
                PerfilId = other.PerfilId, DataAtualizacao = DateTime.UtcNow.AddDays(-1) };
            db.UsuariosClinicas.Add(otherMember);
            await db.SaveChangesAsync();
            otherId = other.Id;
            otherMemberId = otherMember.Id;
            otherUpdatedAt = otherMember.DataAtualizacao;
        }
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        var result = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        result.EnsureSuccessStatusCode();
        Assert.Equal(localId, (await result.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!.Id);
        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        Assert.StartsWith("PBKDF2-SHA256$600000$", (await verify.UsuariosGlobais.SingleAsync(x => x.Id == globalId)).Senha);
        Assert.Equal(weakHash, (await verify.Users.SingleAsync(x => x.Id == localId)).Senha);
        Assert.Equal(otherHash, (await verify.Users.SingleAsync(x => x.Id == otherId)).Senha);
        Assert.Equal(otherUpdatedAt, (await verify.UsuariosClinicas.SingleAsync(x => x.Id == otherMemberId)).DataAtualizacao);
    }
}
