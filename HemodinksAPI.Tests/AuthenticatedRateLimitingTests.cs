using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #374: limite por identidade nas rotas autenticadas e limite proprio para exportacoes.
public sealed class AuthenticatedRateLimitingTests : ApiEndpointTestBase
{
    [Fact]
    public async Task AuthenticatedRoutes_AreLimitedPerIdentity_WithoutAffectingOtherUsersOrAnonymousRoutes()
    {
        using var factory = new HemodinksApiFactory(services =>
            services.PostConfigure<AuthenticationRateLimitOptions>(options => options.AuthenticatedPermitLimit = 5));
        var beta = await SeedClinicaBetaAsync(factory);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, Clinica.DefaultSlug);
        using var doctor = factory.CreateClient();
        await AuthenticateAsync(doctor, beta.Slug, "dra.beta@hemodinks.com");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/licencas/current")).StatusCode);
        using var limited = await admin.GetAsync("/api/licencas/current");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(limited.Headers.CacheControl?.NoStore);
        using (var payload = JsonDocument.Parse(await limited.Content.ReadAsStringAsync()))
        {
            Assert.Equal("rate_limited", payload.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("gmarcone", payload.RootElement.GetRawText());
        }

        // Outra identidade e rotas anonimas seguem livres.
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/licencas/current")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/public/clinicas")).StatusCode);
    }

    [Fact]
    public async Task Exports_HaveTheirOwnLowerLimitPerIdentity()
    {
        using var factory = new HemodinksApiFactory(services =>
        {
            var descriptor = services.FirstOrDefault(item => item.ServiceType == typeof(IFileExportQueue));
            if (descriptor != null) services.Remove(descriptor);
            services.AddSingleton<IFileExportQueue>(new CapturingFileExportQueue());
            services.PostConfigure<AuthenticationRateLimitOptions>(options => options.ExportPermitLimit = 2);
        });
        var beta = await SeedClinicaBetaAsync(factory);
        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, Clinica.DefaultSlug);
        var export = new { resource = "faturamentos-medicos", format = "xlsx", filters = new Dictionary<string, string?>() };

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/api/exports/", export)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await admin.PostAsJsonAsync("/api/exports/", export)).StatusCode);
        // O limite de exportacao nao trava o restante da sessao.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/licencas/current")).StatusCode);

        await AddBetaAdminAsync(factory, beta.Id);
        using var other = factory.CreateClient();
        await AuthenticateAsync(other, beta.Slug, "admin.export@hemodinks.test");
        Assert.Equal(HttpStatusCode.Accepted, (await other.PostAsJsonAsync("/api/exports/", export)).StatusCode);
    }

    private static async Task AddBetaAdminAsync(HemodinksApiFactory factory, int clinicaId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HemodinksAPI.Infrastructure.Data.PlatformDbContext>();
        var hash = new HemodinksAPI.Infrastructure.Utils.PasswordHasher().HashPassword(TestPasswords.Valid);
        const string email = "admin.export@hemodinks.test";
        var user = new User { ClinicaId = clinicaId, Nome = "Admin Export", Email = email, Telefone = "11987654321",
            Senha = hash, PerfilId = Perfil.AdministradorId, Ativo = true };
        db.UsuariosClinicas.Add(new UsuarioClinica { ClinicaId = clinicaId, User = user, PerfilId = Perfil.AdministradorId,
            UsuarioGlobal = new UsuarioGlobal { Nome = "Admin Export", Email = email, Senha = hash, SecurityVersion = Guid.NewGuid() } });
        await db.SaveChangesAsync();
    }
}
