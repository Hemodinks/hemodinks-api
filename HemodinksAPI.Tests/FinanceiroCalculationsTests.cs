using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;

namespace HemodinksAPI.Tests;

public class FinanceiroCalculationsTests
{
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
}
