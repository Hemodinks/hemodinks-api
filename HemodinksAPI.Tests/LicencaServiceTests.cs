using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public class LicencaServiceTests
{
    [Fact]
    public async Task GetOrCreateForMedicoAsync_WhenLicenseDoesNotExist_CreatesTrial()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateMedico();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var licenca = await service.GetOrCreateForMedicoAsync(user.Id, CancellationToken.None);

        Assert.Equal(LicencaPlanos.Trial, licenca.Plano);
        Assert.True(licenca.Ativa);
        Assert.Contains(LicencaFeatures.PacientesVisualizar, licenca.FeaturesEfetivas);
        Assert.DoesNotContain(LicencaFeatures.PacientesGerenciar, licenca.FeaturesEfetivas);
        Assert.Equal(1, await context.Licencas.CountAsync());
    }

    [Fact]
    public async Task HasFeatureAsync_WhenTrialIsActive_DeniesPatientManagement()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateMedico();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var canView = await service.HasFeatureAsync(
            new CurrentUserContext(user.Id, Perfil.MedicosId, user.Nome),
            LicencaFeatures.PacientesVisualizar,
            CancellationToken.None);

        var canManage = await service.HasFeatureAsync(
            new CurrentUserContext(user.Id, Perfil.MedicosId, user.Nome),
            LicencaFeatures.PacientesGerenciar,
            CancellationToken.None);

        Assert.True(canView);
        Assert.False(canManage);
    }

    [Fact]
    public async Task LiberarCompletaAsync_WhenCalled_KeepsPatientManagementUnavailableToDoctors()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateMedico();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var licenca = await service.LiberarCompletaAsync(
            user.Id,
            new LiberarLicencaCompletaRequest { Observacoes = "Compra confirmada" },
            CancellationToken.None);

        var canManage = await service.HasFeatureAsync(
            new CurrentUserContext(user.Id, Perfil.MedicosId, user.Nome),
            LicencaFeatures.PacientesGerenciar,
            CancellationToken.None);

        Assert.Equal(LicencaPlanos.Completa, licenca.Plano);
        Assert.True(licenca.AcessoCompleto);
        Assert.False(canManage);
    }

    [Fact]
    public async Task HasFeatureAsync_WhenTrialExpired_DeniesTrialFeatures()
    {
        await using var context = TestDbContextFactory.Create();
        var user = CreateMedico();
        var now = DateTime.UtcNow;
        context.Users.Add(user);
        context.Licencas.Add(new Licenca
        {
            User = user,
            Plano = LicencaPlanos.Trial,
            Status = LicencaStatus.Ativa,
            DataInicioTrial = now.AddDays(-20),
            DataFimTrial = now.AddDays(-6),
            DataCadastro = now.AddDays(-20)
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var canView = await service.HasFeatureAsync(
            new CurrentUserContext(user.Id, Perfil.MedicosId, user.Nome),
            LicencaFeatures.PacientesVisualizar,
            CancellationToken.None);

        Assert.False(canView);
    }

    private static LicencaService CreateService(HemodinksAPI.Infrastructure.Data.AppDbContext context)
    {
        return new LicencaService(context, Options.Create(new LicencaOptions()));
    }

    private static User CreateMedico()
    {
        return new User
        {
            Nome = "Medico Licenca",
            Email = $"medico.{Guid.NewGuid():N}@email.com",
            Telefone = "+5511999999999",
            Cpf = "52998224725",
            Crm = "12345",
            CrmUf = "SP",
            Senha = "hash",
            DataCadastro = DateTime.UtcNow,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = false,
            PerfilId = Perfil.MedicosId
        };
    }
}
