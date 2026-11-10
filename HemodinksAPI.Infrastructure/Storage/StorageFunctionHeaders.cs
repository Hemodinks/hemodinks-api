namespace HemodinksAPI.Infrastructure.Storage;

public static class StorageFunctionHeaders
{
    // Clínica dona do arquivo (#226), enviada pela API a partir do registro carregado sob o filtro de tenant.
    public const string ClinicaId = "X-Clinica-Id";

    /// <summary>
    /// Lê o X-Clinica-Id recebido pela Function. Ausente: null (API anterior ao #226, layout legado).
    /// Presente e inválido (não numérico, zero, negativo ou repetido): erro, nunca grava.
    /// </summary>
    public static int? ParseClinicaId(IEnumerable<string>? values)
    {
        if (values is null) return null;
        var list = values.ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1
            && int.TryParse(list[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
            && id > 0)
        {
            return id;
        }

        throw new InvalidOperationException("Clinica invalida para armazenar arquivo.");
    }
}
