using HemodinksAPI.Application.Features.Faturamentos;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public class FaturamentoMedicoFinancialSyncTests
{
    [Theory]
    [InlineData("100.50", 100.50)]
    [InlineData("100.5", 100.5)]
    [InlineData("R$ 100.50", 100.50)]
    [InlineData("-100.50", -100.50)]
    [InlineData("R$ 1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1.000", 1000)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("100", 100)]
    [InlineData("0,00", 0)]
    public void ParseCurrency_PreservesMedicalAmountsAndLegacyThousands(string raw, decimal expected)
    {
        Assert.Equal(expected, FaturamentoMedicoSync.ParseCurrency(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem valor registrado")]
    public void ParseCurrency_DoesNotInventZeroForMissingOrNonNumericValues(string? raw)
    {
        Assert.Null(FaturamentoMedicoSync.ParseCurrency(raw));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureSynced_PreservesDeclaredSettlementAndPartialAmount(bool paid)
    {
        var record = new Paciente
        {
            Id = 176,
            ClinicaId = 1,
            StatusPago = paid,
            Pagamento = "100.50",
            RepasseGlosa = "20.25",
            Procedimentos = [new PacienteProcedimento { ValorReferencia = 200m }]
        };

        var billing = FaturamentoMedicoSync.EnsureSynced(record, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(200m, billing.HonorariosCirurgiao);
        Assert.Equal(100.50m, billing.RepasseMedico);
        Assert.Equal(20.25m, billing.ValorGlosa);
        Assert.Equal(paid, record.StatusPago);
        Assert.Equal(paid, billing.ConferenciaPagamentoRealizada);
        Assert.Null(billing.DataPagamento);
        Assert.Null(billing.GuiaAutorizacaoConvenio);
        Assert.Null(billing.GuiaInternacaoOuSadt);
        Assert.Equal(record.ClinicaId, billing.ClinicaId);
        Assert.Equal(record.Id, billing.PacienteId);
    }

    [Fact]
    public void EnsureSynced_MissingValuesPreserveStoredAmountsWithoutConfirmingSettlement()
    {
        var billing = new FaturamentoMedico
        {
            HonorariosCirurgiao = 200m,
            ValorGlosa = 20m,
            RepasseMedico = 80m
        };
        var record = new Paciente { StatusPago = false, FaturamentoMedico = billing };

        var result = FaturamentoMedicoSync.EnsureSynced(record, DateTime.UtcNow);

        Assert.Same(billing, result);
        Assert.Equal(200m, result.HonorariosCirurgiao);
        Assert.Equal(20m, result.ValorGlosa);
        Assert.Equal(80m, result.RepasseMedico);
        Assert.False(record.StatusPago);
        Assert.False(result.ConferenciaPagamentoRealizada);
    }
}
