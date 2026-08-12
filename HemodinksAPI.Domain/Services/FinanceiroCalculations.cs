using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Domain.Services;

public static class FinanceiroCalculations
{
    public static decimal CalculatePresentedValue(decimal quantidade, decimal pesoPercentual, decimal valorUnitario)
    {
        if (quantidade <= 0 || pesoPercentual < 0 || valorUnitario < 0)
            throw new InvalidOperationException("Quantidade, peso e valor do item devem ser validos.");

        return decimal.Round(quantidade * valorUnitario * pesoPercentual / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static void Recalculate(Faturamento faturamento)
    {
        faturamento.ValorApresentado = faturamento.Itens
            .Where(x => x.Status != FaturamentoItemStatus.Cancelado)
            .Sum(x => x.ValorApresentado);
        faturamento.ValorGlosado = faturamento.Glosas.Sum(x => x.ValorGlosado);
        faturamento.ValorGlosaRecuperada = faturamento.Glosas
            .SelectMany(x => x.Recursos)
            .Where(x => x.Status is RecursoGlosaStatus.Aceito or RecursoGlosaStatus.AceitoParcialmente)
            .Sum(x => x.ValorRecuperado);
        faturamento.ValorReconhecido = Math.Max(0m,
            faturamento.ValorApresentado - faturamento.ValorGlosado + faturamento.ValorGlosaRecuperada);
    }

    public static void Recalculate(ContaReceber conta, DateTime utcNow)
    {
        conta.ValorRecebido = conta.Recebimentos.Where(x => !x.Estornado).Sum(x => x.ValorRecebido);
        conta.SaldoAberto = Math.Max(0m, conta.ValorAjustado - conta.ValorRecebido);

        if (conta.Status == ContaReceberStatus.Cancelado)
            return;

        conta.Status = conta.ValorRecebido >= conta.ValorAjustado
            ? ContaReceberStatus.Recebido
            : conta.ValorRecebido > 0
                ? ContaReceberStatus.ParcialmenteRecebido
                : conta.DataVencimento.Date < utcNow.Date
                    ? ContaReceberStatus.Vencido
                    : ContaReceberStatus.Aberto;
    }

    public static void ReconcileAccountsWithRecognizedValue(Faturamento faturamento, DateTime utcNow)
    {
        var accounts = faturamento.ContasReceber.Where(x => x.Status != ContaReceberStatus.Cancelado).OrderBy(x => x.Id).ToList();
        if (accounts.Count == 0) return;
        var originalTotal = accounts.Sum(x => x.ValorOriginal);
        if (originalTotal <= 0) return;
        var remaining = faturamento.ValorReconhecido;
        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            var adjusted = index == accounts.Count - 1
                ? remaining
                : decimal.Round(faturamento.ValorReconhecido * account.ValorOriginal / originalTotal, 2, MidpointRounding.AwayFromZero);
            if (adjusted < account.Recebimentos.Where(x => !x.Estornado).Sum(x => x.ValorRecebido))
                throw new InvalidOperationException("O valor reconhecido nao pode ficar abaixo do total ja recebido.");
            account.ValorAjustado = adjusted;
            remaining -= adjusted;
            Recalculate(account, utcNow);
            account.DataAtualizacao = utcNow;
        }
    }

    public static void RecalculatePaymentStatus(Faturamento faturamento)
    {
        if (faturamento.Status == FaturamentoStatus.Cancelado) return;
        var accounts = faturamento.ContasReceber.Where(x => x.Status != ContaReceberStatus.Cancelado).ToList();
        if (accounts.Count == 0) return;
        var adjusted = accounts.Sum(x => x.ValorAjustado);
        var received = accounts.Sum(x => x.Recebimentos.Where(r => !r.Estornado).Sum(r => r.ValorRecebido));
        faturamento.Status = adjusted > 0 && received >= adjusted
            ? FaturamentoStatus.Pago
            : received > 0 ? FaturamentoStatus.ParcialmentePago
            : faturamento.DataRetorno.HasValue ? FaturamentoStatus.Aprovado : FaturamentoStatus.Enviado;
    }
}
