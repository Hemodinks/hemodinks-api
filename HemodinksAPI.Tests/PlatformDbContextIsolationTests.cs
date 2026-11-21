using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class PlatformDbContextIsolationTests
{
    [Fact]
    public async Task Platform_context_does_not_mutate_or_share_request_tenant_scope()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var requestTenant = new ClinicaContext();
        requestTenant.SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);

        await using (var platform = new PlatformDbContext(options))
        {
            platform.Clinicas.Add(new Clinica { Id = 2, Nome = "Outra", Slug = "outra", Ativa = true });
            platform.Users.AddRange(
                CreateUser(Clinica.DefaultId, "first@example.com"),
                CreateUser(2, "second@example.com"));
            await platform.SaveChangesAsync();

            Assert.Equal(2, await platform.Users.CountAsync());
        }

        await using var requestContext = new AppDbContext(options, requestTenant);
        Assert.Single(await requestContext.Users.ToListAsync());
        Assert.Equal(Clinica.DefaultId, requestTenant.ClinicaId);
        Assert.False(requestTenant.IsPlatformScope);
    }

    // #294/#359: interfaces de escopo de plataforma nunca no contexto de tenant da requisicao.
    public static TheoryData<Type> PlatformScopedInterfaces =>
    [
        typeof(IPlatformSessionMonitorDbContext),
        typeof(ISensitiveIdentityDbContext),
        typeof(ITemporaryAccessDbContext),
        typeof(IPlatformPasswordResetDbContext),
        typeof(IPlatformTeamDbContext),
        typeof(IPlatformClinicDbContext),
        typeof(ISessionDbContext),
    ];

    [Theory]
    [MemberData(nameof(PlatformScopedInterfaces))]
    public void Platform_scoped_interface_is_only_implemented_and_resolved_by_platform_context(Type contract)
    {
        Assert.False(contract.IsAssignableFrom(typeof(AppDbContext)), $"{contract.Name} no AppDbContext de tenant");
        Assert.True(contract.IsAssignableFrom(typeof(PlatformDbContext)));

        using var factory = new HemodinksApiFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<PlatformDbContext>(scope.ServiceProvider.GetRequiredService(contract));
    }

    private static User CreateUser(int clinicaId, string email)
    {
        return new User
        {
            ClinicaId = clinicaId,
            Nome = email,
            Email = email,
            Telefone = "+5511999999999",
            Senha = "hash",
            PerfilId = Perfil.AdministradorId,
            Ativo = true
        };
    }
}
