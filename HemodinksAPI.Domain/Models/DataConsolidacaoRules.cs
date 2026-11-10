namespace HemodinksAPI.Domain.Models;

// Faixa permitida para o ano da data da consolidação (Paciente.DataAtendimento).
// O HemoDinks começou a operar em 2026; o limite superior acompanha o ano corrente (+1).
// Registros legados fora da faixa continuam válidos para uso, mas são sinalizados como pendência.
public static class DataConsolidacaoRules
{
    public const int AnoMinimo = 2026;

    // Brasil sem horário de verão desde 2019: o ano civil local é UTC-3.
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static int AnoMaximo(DateTimeOffset agora) => agora.ToOffset(BrasiliaOffset).Year + 1;

    public static bool IsConforme(DateTime dataConsolidacao, int anoMaximo) =>
        dataConsolidacao.Year >= AnoMinimo && dataConsolidacao.Year <= anoMaximo;

    public static string MensagemForaDaFaixa(int anoMaximo) =>
        $"O ano da data da consolidação deve estar entre {AnoMinimo} e {anoMaximo}.";

    public static string MensagemApagarProibido(int anoMaximo) =>
        "Não é permitido apagar a data de uma cirurgia já consolidada. "
        + $"Corrija o ano da data da consolidação para um valor entre {AnoMinimo} e {anoMaximo}.";

    public static void Validate(DateTime? dataConsolidacao, DateTimeOffset agora)
    {
        if (!dataConsolidacao.HasValue) return;
        var anoMaximo = AnoMaximo(agora);
        if (!IsConforme(dataConsolidacao.Value, anoMaximo))
            throw new InvalidOperationException(MensagemForaDaFaixa(anoMaximo));
    }

    // Edição: data legada inalterada é tolerada; data alterada precisa estar na faixa;
    // cadastro pendente (data fora da faixa) não pode ter a data apagada, para não mascarar o faturamento.
    public static void ValidateUpdate(DateTime? dataAtual, DateTime? novaData, DateTimeOffset agora)
    {
        if (novaData?.Date == dataAtual?.Date) return;
        var anoMaximo = AnoMaximo(agora);
        if (!novaData.HasValue)
        {
            if (dataAtual.HasValue && !IsConforme(dataAtual.Value, anoMaximo))
                throw new InvalidOperationException(MensagemApagarProibido(anoMaximo));
            return;
        }

        Validate(novaData, agora);
    }
}
