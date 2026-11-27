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
    private static readonly DateTimeOffset Agora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_SeesPendingSurgeriesAmountsAndTwelveMonthSeries()
    {
        var options = NewOptions();
        await SeedAsync(options);
        await using var context = TenantContext(options, Clinica.DefaultId, Clinica.DefaultSlug);

        var result = await Handler(context).Handle(new GetDashboardSummaryQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = 1
        }, default);

        Assert.Equal(1, result.CirurgiasNaoConsolidadasCount);
        Assert.Equal(2, result.CirurgiasAReceberCount);
        // A receber: (1000 - 100) + 500; a cirurgia paga e a nao consolidada ficam de fora.
        Assert.Equal(1400m, result.ValorAReceber);
        var mensal = Assert.IsAssignableFrom<IReadOnlyList<DashboardMonthlyBillingDto>>(result.FaturamentoMensal);
        Assert.Equal(12, mensal.Count);
        Assert.Equal((2025, 12), (mensal[0].Ano, mensal[0].Mes));
        Assert.Equal((2026, 11), (mensal[^1].Ano, mensal[^1].Mes));
        var novembro = mensal[^1];
        Assert.Equal(2, novembro.Cirurgias);
        Assert.Equal(1800m, novembro.Faturado);
        Assert.Equal(750m, novembro.Recebido);
        var outubro = mensal[^2];
        Assert.Equal((1, 500m, 0m), (outubro.Cirurgias, outubro.Faturado, outubro.Recebido));
        // Fora da janela de 12 meses (nov/2025) e meses sem cirurgia aparecem zerados.
        Assert.All(mensal.Take(10), item => Assert.Equal(0, item.Cirurgias));
    }

    [Fact]
    public async Task Patient_ReceivesNoSurgicalBillingFigures()
    {
        var options = NewOptions();
        await SeedAsync(options);
        await using var context = TenantContext(options, Clinica.DefaultId, Clinica.DefaultSlug);

        var result = await Handler(context).Handle(new GetDashboardSummaryQuery
        {
            CurrentPerfilId = Perfil.PacientesId,
            CurrentUserId = 1
        }, default);

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

        await using (var contextA = TenantContext(options, Clinica.DefaultId, Clinica.DefaultSlug))
        {
            var resultA = await Handler(contextA).Handle(new GetDashboardSummaryQuery
            {
                CurrentPerfilId = Perfil.AdministradorId,
                CurrentUserId = 1
            }, default);
            Assert.Equal(2, resultA.CirurgiasAReceberCount);
            Assert.Equal(1400m, resultA.ValorAReceber);
            Assert.Equal(3, resultA.FaturamentoMensal!.Sum(item => item.Cirurgias));
        }

        await using var contextB = TenantContext(options, ClinicaB, "clinica-b-dashboard");
        var resultB = await Handler(contextB).Handle(new GetDashboardSummaryQuery
        {
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserId = 1
        }, default);
        Assert.Equal(0, resultB.CirurgiasNaoConsolidadasCount);
        Assert.Equal(1, resultB.CirurgiasAReceberCount);
        Assert.Equal(9999m, resultB.ValorAReceber);
        Assert.Equal(1, resultB.FaturamentoMensal!.Sum(item => item.Cirurgias));
    }

    private static GetDashboardSummaryQueryHandler Handler(AppDbContext context) =>
        new(context, NullLogger<GetDashboardSummaryQueryHandler>.Instance, new FixedClock(Agora));

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static AppDbContext TenantContext(DbContextOptions<AppDbContext> options, int clinicaId, string slug)
    {
        var tenant = new ClinicaContext();
        tenant.SetCurrent(clinicaId, slug);
        return new AppDbContext(options, tenant, TestCpfProtection.Instance);
    }

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
    {
        await using var seed = new PlatformDbContext(options, TestCpfProtection.Instance);
        await seed.Database.EnsureCreatedAsync();
        seed.Clinicas.Add(new Clinica { Id = ClinicaB, Nome = "Clinica B", Slug = "clinica-b-dashboard" });
        await seed.SaveChangesAsync();

        // Clinica A: paga em nov, a receber em nov (com glosa), a receber em out, nao consolidada e uma fora da janela.
        Add(seed, Clinica.DefaultId, "A-paga", new DateTime(2026, 11, 3), pago: true, honorarios: 800m, recebido: 750m);
        Add(seed, Clinica.DefaultId, "A-receber-glosa", new DateTime(2026, 11, 10), pago: false, honorarios: 1000m, glosa: 100m);
        Add(seed, Clinica.DefaultId, "A-receber", new DateTime(2026, 10, 20), pago: false, honorarios: 500m);
        Add(seed, Clinica.DefaultId, "A-nao-consolidada", null, pago: false, honorarios: 300m);
        Add(seed, Clinica.DefaultId, "A-fora-janela", new DateTime(2025, 11, 30), pago: true, honorarios: 200m, recebido: 200m);
        // Clinica B: nao pode aparecer no painel da A.
        Add(seed, ClinicaB, "B-receber", new DateTime(2026, 11, 5), pago: false, honorarios: 9999m);
        await seed.SaveChangesAsync();
    }

    private static void Add(PlatformDbContext seed, int clinicaId, string nome, DateTime? consolidacao, bool pago,
        decimal honorarios, decimal? glosa = null, decimal? recebido = null)
    {
        var user = new User
        {
            ClinicaId = clinicaId, Nome = nome, Email = $"{nome}-{clinicaId}@example.invalid", Telefone = "11999999999",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid), PerfilId = Perfil.PacientesId, Ativo = true
        };
        var paciente = new Paciente
        {
            ClinicaId = clinicaId, User = user, NomePaciente = nome, DataAtendimento = consolidacao, StatusPago = pago
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
