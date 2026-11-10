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

    public static void Validate(DateTime? dataConsolidacao, DateTimeOffset agora)
    {
        if (!dataConsolidacao.HasValue) return;
        var anoMaximo = AnoMaximo(agora);
        if (!IsConforme(dataConsolidacao.Value, anoMaximo))
            throw new InvalidOperationException(MensagemForaDaFaixa(anoMaximo));
    }
}
