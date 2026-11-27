using HemodinksAPI.Application.Features.Dashboard.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

// Indicadores do painel a partir do registro de cirurgia (Paciente + FaturamentoMedico).
public sealed class DashboardSurgicalBillingSummaryTests
{
    private const int ClinicaB = 7302;
    private const int ClinicaSemFaturamento = 7303;
    private static readonly DateTimeOffset Agora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_SeesPendingSurgeriesAmountsAndTwelveMonthSeries()
    {
        var options = NewOptions();
        await SeedAsync(options);

        var result = await QueryAsync(options, Clinica.DefaultId, Perfil.AdministradorId, userId: 1);

        Assert.Equal(1, result.CirurgiasNaoConsolidadasCount);
        Assert.Equal(2, result.CirurgiasAReceberCount);
        // A receber: (1000 - 100) + 500; a cirurgia paga e a nao consolidada ficam de fora.
        Assert.Equal(1400m, result.ValorAReceber);
        var mensal = Assert.IsAssignableFrom<IReadOnlyList<DashboardMonthlyBillingDto>>(result.FaturamentoMensal);
        Assert.Equal(12, mensal.Count);
        Assert.Equal((2025, 12), (mensal[0].Ano, mensal[0].Mes));
        Assert.Equal((2026, 11), (mensal[^1].Ano, mensal[^1].Mes));
        Assert.Equal((2, 1800m, 750m), (mensal[^1].Cirurgias, mensal[^1].Faturado, mensal[^1].Recebido));
        Assert.Equal((1, 500m, 0m), (mensal[^2].Cirurgias, mensal[^2].Faturado, mensal[^2].Recebido));
        // Fora da janela de 12 meses (nov/2025) e meses sem cirurgia aparecem zerados.
        Assert.All(mensal.Take(10), item => Assert.Equal(0, item.Cirurgias));
    }

    [Fact]
    public async Task Patient_ReceivesNoSurgicalBillingFigures()
    {
        var options = NewOptions();
        await SeedAsync(options);

        var result = await QueryAsync(options, Clinica.DefaultId, Perfil.PacientesId, userId: 1);

        Assert.Null(result.CirurgiasNaoConsolidadasCount);
        Assert.Null(result.CirurgiasAReceberCount);
        Assert.Null(result.ValorAReceber);
        Assert.Null(result.FaturamentoMensal);
    }

    // Tenant (AGENTS.md): cirurgias da Clinica B nunca entram nos numeros da Clinica A, e vice-versa.
    [Fact]
    public async Task ClinicB_SurgeriesAreNotCountedForClinicA()
    {
        var options = NewOptions();
        await SeedAsync(options);

        var resultA = await QueryAsync(options, Clinica.DefaultId, Perfil.AdministradorId, userId: 1);
        Assert.Equal(2, resultA.CirurgiasAReceberCount);
        Assert.Equal(1400m, resultA.ValorAReceber);
        Assert.Equal(3, resultA.FaturamentoMensal!.Sum(item => item.Cirurgias));

        var resultB = await QueryAsync(options, ClinicaB, Perfil.AdministradorId, userId: 1);
        Assert.Equal(0, resultB.CirurgiasNaoConsolidadasCount);
        Assert.Equal(1, resultB.CirurgiasAReceberCount);
        Assert.Equal(9999m, resultB.ValorAReceber);
        Assert.Equal(1, resultB.FaturamentoMensal!.Sum(item => item.Cirurgias));
    }

    // Revisao de seguranca: o painel usa o escopo do faturamento. O medico nao soma valores de um colega
    // do mesmo grupo medico (o escopo de pacientes incluiria; o modulo de faturamento nao).
    [Fact]
    public async Task Doctor_DoesNotSeeAmountsOfSameGroupColleague()
    {
        var options = NewOptions();
        var (medico, colega) = await SeedDoctorsInSameGroupAsync(options);

        var result = await QueryAsync(options, Clinica.DefaultId, Perfil.MedicosId, medico);

        Assert.Equal(1, result.CirurgiasAReceberCount);
        Assert.Equal(300m, result.ValorAReceber);
        Assert.Equal(300m, result.FaturamentoMensal!.Sum(item => item.Faturado));
        var colegaResult = await QueryAsync(options, Clinica.DefaultId, Perfil.MedicosId, colega);
        Assert.Equal(700m, colegaResult.ValorAReceber);
    }

    [Fact]
    public async Task Team_SeesOnlyItsMembers_AndTeamWithoutIdSeesNothing()
    {
        var options = NewOptions();
        var (medico, _) = await SeedDoctorsInSameGroupAsync(options);
        int equipeId;
        await using (var seed = new PlatformDbContext(options, TestCpfProtection.Instance))
        {
            var login = NewUser(Clinica.DefaultId, "equipe-login", Perfil.EquipeId);
            var equipe = new Equipe { ClinicaId = Clinica.DefaultId, Nome = "Equipe 1", UsuarioLogin = login };
            seed.Equipes.Add(equipe);
            seed.EquipeMembros.Add(new EquipeMembro { ClinicaId = Clinica.DefaultId, Equipe = equipe, UserId = medico, Ativo = true });
            await seed.SaveChangesAsync();
            equipeId = equipe.Id;
        }

        var result = await QueryAsync(options, Clinica.DefaultId, Perfil.EquipeId, userId: 1, equipeId);
        Assert.Equal(1, result.CirurgiasAReceberCount);
        Assert.Equal(300m, result.ValorAReceber);

        var semEquipe = await QueryAsync(options, Clinica.DefaultId, Perfil.EquipeId, userId: 1);
        Assert.Equal(0, semEquipe.CirurgiasAReceberCount);
        Assert.Equal(0m, semEquipe.ValorAReceber);
    }

    [Fact]
    public async Task Controller_SeesWholeClinic()
    {
        var options = NewOptions();
        await SeedDoctorsInSameGroupAsync(options);

        var result = await QueryAsync(options, Clinica.DefaultId, Perfil.ControllerId, userId: 1);

        Assert.Equal(2, result.CirurgiasAReceberCount);
        Assert.Equal(1000m, result.ValorAReceber);
    }

    // Revisao de seguranca: mesma regra do ClinicaModuleAccessMiddleware para o faturamento.
    [Theory]
    [InlineData(Perfil.MedicosId, false)]
    [InlineData(Perfil.ControllerId, false)]
    [InlineData(Perfil.EquipeId, false)]
    [InlineData(Perfil.AdministradorId, true)]
    [InlineData(Perfil.SuperAdministradorId, true)]
    public async Task ClinicWithoutBillingModule_HidesFiguresExceptForAdministrators(int perfilId, bool visible)
    {
        var options = NewOptions();
        await SeedAsync(options);

        var result = await QueryAsync(options, ClinicaSemFaturamento, perfilId, userId: 1);

        Assert.Equal(visible, result.CirurgiasAReceberCount.HasValue);
        Assert.Equal(visible, result.ValorAReceber.HasValue);
        Assert.Equal(visible, result.FaturamentoMensal != null);
    }

    [Fact]
    public async Task WithoutClinicDirectory_FailsClosed()
    {
        var options = NewOptions();
        await SeedAsync(options);
        await using var context = TenantContext(options, Clinica.DefaultId, Clinica.DefaultSlug, out _);
        var handler = new GetDashboardSummaryQueryHandler(context, NullLogger<GetDashboardSummaryQueryHandler>.Instance, new FixedClock(Agora));

        var result = await handler.Handle(new GetDashboardSummaryQuery { CurrentPerfilId = Perfil.MedicosId, CurrentUserId = 1 }, default);

        Assert.Null(result.ValorAReceber);
    }

    private static async Task<DashboardSummaryDto> QueryAsync(DbContextOptions<AppDbContext> options, int clinicaId, int perfilId,
        int userId, int? equipeId = null)
    {
        var slug = clinicaId == Clinica.DefaultId ? Clinica.DefaultSlug : $"clinica-{clinicaId}";
        await using var context = TenantContext(options, clinicaId, slug, out var tenant);
        var handler = new GetDashboardSummaryQueryHandler(context, NullLogger<GetDashboardSummaryQueryHandler>.Instance,
            new FixedClock(Agora), context, tenant);
        return await handler.Handle(new GetDashboardSummaryQuery
        {
            CurrentPerfilId = perfilId,
            CurrentUserId = userId,
            CurrentEquipeId = equipeId
        }, default);
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static AppDbContext TenantContext(DbContextOptions<AppDbContext> options, int clinicaId, string slug, out ClinicaContext tenant)
    {
        tenant = new ClinicaContext();
        tenant.SetCurrent(clinicaId, slug);
        return new AppDbContext(options, tenant, TestCpfProtection.Instance);
    }

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        await using var seed = new PlatformDbContext(options, TestCpfProtection.Instance);
        await seed.Database.EnsureCreatedAsync();
        seed.Clinicas.Add(new Clinica { Id = ClinicaB, Nome = "Clinica B", Slug = $"clinica-{ClinicaB}", Plano = ClinicaPlanos.Completa });
        seed.Clinicas.Add(new Clinica
        {
            Id = ClinicaSemFaturamento, Nome = "Clinica sem faturamento", Slug = $"clinica-{ClinicaSemFaturamento}",
            Plano = ClinicaPlanos.Parcial, ModulosLiberados = $"{ClinicaModulos.Pacientes},{ClinicaModulos.Usuarios}"
        });
        await seed.SaveChangesAsync();

        // Clinica A: paga em nov, a receber em nov (com glosa), a receber em out, nao consolidada e uma fora da janela.
        Add(seed, Clinica.DefaultId, "A-paga", new DateTime(2026, 11, 3), pago: true, honorarios: 800m, recebido: 750m);
        Add(seed, Clinica.DefaultId, "A-receber-glosa", new DateTime(2026, 11, 10), pago: false, honorarios: 1000m, glosa: 100m);
        Add(seed, Clinica.DefaultId, "A-receber", new DateTime(2026, 10, 20), pago: false, honorarios: 500m);
        Add(seed, Clinica.DefaultId, "A-nao-consolidada", null, pago: false, honorarios: 300m);
        Add(seed, Clinica.DefaultId, "A-fora-janela", new DateTime(2025, 11, 30), pago: true, honorarios: 200m, recebido: 200m);
        // Clinica B: nao pode aparecer no painel da A.
        Add(seed, ClinicaB, "B-receber", new DateTime(2026, 11, 5), pago: false, honorarios: 9999m);
        Add(seed, ClinicaSemFaturamento, "S-receber", new DateTime(2026, 11, 5), pago: false, honorarios: 123m);
        await seed.SaveChangesAsync();
    }

    // Dois medicos no mesmo grupo medico, cada um com uma cirurgia a receber.
    private static async Task<(int Medico, int Colega)> SeedDoctorsInSameGroupAsync(DbContextOptions<AppDbContext> options)
    {
        await using var seed = new PlatformDbContext(options, TestCpfProtection.Instance);
        await seed.Database.EnsureCreatedAsync();
        var medico = NewUser(Clinica.DefaultId, "medico", Perfil.MedicosId);
        var colega = NewUser(Clinica.DefaultId, "colega", Perfil.MedicosId);
        seed.Users.AddRange(medico, colega);
        var grupo = new GrupoMedico { ClinicaId = Clinica.DefaultId, Nome = "Grupo comum" };
        grupo.Membros.Add(new GrupoMedicoUsuario { ClinicaId = Clinica.DefaultId, GrupoMedico = grupo, User = medico });
        grupo.Membros.Add(new GrupoMedicoUsuario { ClinicaId = Clinica.DefaultId, GrupoMedico = grupo, User = colega });
        seed.GruposMedicos.Add(grupo);
        await seed.SaveChangesAsync();

        Add(seed, Clinica.DefaultId, "do-medico", new DateTime(2026, 11, 2), pago: false, honorarios: 300m, medicoUserId: medico.Id);
        Add(seed, Clinica.DefaultId, "do-colega", new DateTime(2026, 11, 4), pago: false, honorarios: 700m, medicoUserId: colega.Id);
        await seed.SaveChangesAsync();
        return (medico.Id, colega.Id);
    }

    private static User NewUser(int clinicaId, string nome, int perfilId) => new()
    {
        ClinicaId = clinicaId, Nome = nome, Email = $"{nome}-{clinicaId}-{Guid.NewGuid():N}@example.invalid", Telefone = "11999999999",
        Senha = new PasswordHasher().HashPassword(TestPasswords.Valid), PerfilId = perfilId, Ativo = true
    };

    private static void Add(PlatformDbContext seed, int clinicaId, string nome, DateTime? consolidacao, bool pago,
        decimal honorarios, decimal? glosa = null, decimal? recebido = null, int? medicoUserId = null)
    {
        var paciente = new Paciente
        {
            ClinicaId = clinicaId, User = NewUser(clinicaId, nome, Perfil.PacientesId), NomePaciente = nome,
            DataAtendimento = consolidacao, StatusPago = pago, MedicoUserId = medicoUserId
        };
        paciente.FaturamentoMedico = new FaturamentoMedico
        {
            ClinicaId = clinicaId, Paciente = paciente, HonorariosCirurgiao = honorarios, ValorGlosa = glosa,
            RepasseMedico = recebido
        };
        seed.Pacientes.Add(paciente);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
