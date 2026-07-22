using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using HemodinksAPI.Application.Features.Financeiro;

namespace HemodinksAPI.Tests;

public class FinanceiroCalculationsTests
{
    [Theory]
    [InlineData("R$ 1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    public void LegacyFallback_ParsesSupportedCurrencyFormats(string raw, decimal expected)
    {
        Assert.True(HemodinksAPI.Application.Features.Financeiro.LegacyFinanceiroFallback.TryParseCurrency(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void LegacyFallback_DoesNotInventValueForInvalidText()
    {
        Assert.False(HemodinksAPI.Application.Features.Financeiro.LegacyFinanceiroFallback.TryParseCurrency("pago via permuta", out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task PatientSummary_UsesLegacyFallback_and_flags_invalid_values()
    {
        await using var db = TestDbContextFactory.Create();
        var validUser = new User { Nome = "Legado válido", Telefone = "1", Email = "valid@legacy.test", Senha = "hash", PerfilId = Perfil.PacientesId };
        var invalidUser = new User { Nome = "Legado inválido", Telefone = "2", Email = "invalid@legacy.test", Senha = "hash", PerfilId = Perfil.PacientesId };
        db.Users.AddRange(validUser, invalidUser); await db.SaveChangesAsync();
        var valid = new Paciente { UserId = validUser.Id, NomePaciente = validUser.Nome, Pagamento = "R$ 1.234,56", RepasseGlosa = "R$ 234,56", StatusPago = true };
        var invalid = new Paciente { UserId = invalidUser.Id, NomePaciente = invalidUser.Nome, Pagamento = "pago via permuta", RepasseGlosa = "sem informação" };
        db.Pacientes.AddRange(valid, invalid); await db.SaveChangesAsync();
        var handler = new ObterPacienteFinanceiroResumoQueryHandler(db);

        var validSummary = await handler.Handle(new ObterPacienteFinanceiroResumoQuery(valid.Id, 1, Perfil.AdministradorId), default);
        Assert.Equal("Legado", validSummary.OrigemDados); Assert.Equal(1234.56m, validSummary.ValorApresentado);
        Assert.Equal(234.56m, validSummary.ValorGlosado); Assert.Equal(1000m, validSummary.ValorRecebido); Assert.Empty(validSummary.Avisos);

        var invalidSummary = await handler.Handle(new ObterPacienteFinanceiroResumoQuery(invalid.Id, 1, Perfil.AdministradorId), default);
        Assert.Equal("Requer conciliacao", invalidSummary.StatusFinanceiro); Assert.Equal(0m, invalidSummary.ValorApresentado);
        Assert.Equal(2, invalidSummary.Avisos.Count);
    }
    [Fact]
    public void RecalculateFaturamento_UsesItemsGlosasAndAcceptedAppeals()
    {
        var faturamento = new Faturamento
        {
            Itens =
            [
                new FaturamentoItem { ValorApresentado = 1000m, Status = FaturamentoItemStatus.Apresentado },
                new FaturamentoItem { ValorApresentado = 500m, Status = FaturamentoItemStatus.Apresentado },
                new FaturamentoItem { ValorApresentado = 999m, Status = FaturamentoItemStatus.Cancelado }
            ],
            Glosas =
            [
                new Glosa
                {
                    ValorGlosado = 300m,
                    Recursos =
                    [
                        new RecursoGlosa { ValorRecuperado = 100m, Status = RecursoGlosaStatus.AceitoParcialmente },
                        new RecursoGlosa { ValorRecuperado = 50m, Status = RecursoGlosaStatus.Negado }
                    ]
                }
            ]
        };

        FinanceiroCalculations.Recalculate(faturamento);

        Assert.Equal(1500m, faturamento.ValorApresentado);
        Assert.Equal(300m, faturamento.ValorGlosado);
        Assert.Equal(100m, faturamento.ValorGlosaRecuperada);
        Assert.Equal(1300m, faturamento.ValorReconhecido);
    }

    [Fact]
    public void RecalculateConta_IgnoresReversedReceiptsAndSetsPartialStatus()
    {
        var conta = new ContaReceber
        {
            ValorAjustado = 1000m,
            DataVencimento = DateTime.UtcNow.AddDays(10),
            Recebimentos =
            [
                new Recebimento { ValorRecebido = 400m },
                new Recebimento { ValorRecebido = 200m, Estornado = true }
            ]
        };

        FinanceiroCalculations.Recalculate(conta, DateTime.UtcNow);

        Assert.Equal(400m, conta.ValorRecebido);
        Assert.Equal(600m, conta.SaldoAberto);
        Assert.Equal(ContaReceberStatus.ParcialmenteRecebido, conta.Status);
    }

    [Fact]
    public void RecalculateConta_SetsOverdueOnlyWithoutReceipt()
    {
        var conta = new ContaReceber { ValorAjustado = 250m, DataVencimento = DateTime.UtcNow.AddDays(-1) };

        FinanceiroCalculations.Recalculate(conta, DateTime.UtcNow);

        Assert.Equal(ContaReceberStatus.Vencido, conta.Status);
        Assert.Equal(250m, conta.SaldoAberto);
    }

    [Fact]
    public void CalculatePresentedValue_AppliesQuantityAndWeightWithCurrencyRounding()
    {
        Assert.Equal(666.67m, FinanceiroCalculations.CalculatePresentedValue(2m, 33.3333m, 1000m));
    }

    [Fact]
    public void ReconcileAccounts_DistributesRecognizedValueAcrossInstallments()
    {
        var faturamento = new Faturamento
        {
            ValorReconhecido = 800m,
            ContasReceber =
            [
                new ContaReceber { Id = 1, ValorOriginal = 600m, ValorAjustado = 600m, DataVencimento = DateTime.UtcNow.AddDays(10) },
                new ContaReceber { Id = 2, ValorOriginal = 400m, ValorAjustado = 400m, DataVencimento = DateTime.UtcNow.AddDays(20) }
            ]
        };

        FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(faturamento, DateTime.UtcNow);

        Assert.Equal(480m, faturamento.ContasReceber.First().ValorAjustado);
        Assert.Equal(320m, faturamento.ContasReceber.Last().ValorAjustado);
        Assert.Equal(800m, faturamento.ContasReceber.Sum(x => x.SaldoAberto));
    }

    [Fact]
    public void ReconcileAccounts_RejectsRecognizedValueBelowAlreadyReceivedAmount()
    {
        var faturamento = new Faturamento
        {
            ValorReconhecido = 100m,
            ContasReceber =
            [
                new ContaReceber
                {
                    ValorOriginal = 500m,
                    ValorAjustado = 500m,
                    DataVencimento = DateTime.UtcNow,
                    Recebimentos = [new Recebimento { ValorRecebido = 200m }]
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(faturamento, DateTime.UtcNow));
    }

    [Fact]
    public void RecalculateFaturamento_HandlesTotalGlosaAndFullyAcceptedAppeal()
    {
        var faturamento = new Faturamento
        {
            Itens = [new FaturamentoItem { ValorApresentado = 1000m, Status = FaturamentoItemStatus.GlosadoTotal }],
            Glosas = [new Glosa { ValorGlosado = 1000m, Recursos = [new RecursoGlosa { ValorRecuperado = 1000m, Status = RecursoGlosaStatus.Aceito }] }]
        };
        FinanceiroCalculations.Recalculate(faturamento);
        Assert.Equal(1000m, faturamento.ValorGlosado);
        Assert.Equal(1000m, faturamento.ValorGlosaRecuperada);
        Assert.Equal(1000m, faturamento.ValorReconhecido);
    }

    [Fact]
    public void RecalculateConta_SumsMultipleReceiptsAndSetsPaidStatus()
    {
        var conta = new ContaReceber { ValorAjustado = 1000m, DataVencimento = DateTime.UtcNow.AddDays(1),
            Recebimentos = [new Recebimento { ValorRecebido = 400m }, new Recebimento { ValorRecebido = 600m }] };
        FinanceiroCalculations.Recalculate(conta, DateTime.UtcNow);
        Assert.Equal(1000m, conta.ValorRecebido);
        Assert.Equal(0m, conta.SaldoAberto);
        Assert.Equal(ContaReceberStatus.Recebido, conta.Status);
    }
}
