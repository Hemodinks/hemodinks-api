using System.Globalization;

namespace HemodinksAPI.Application.Storage;

/// <summary>
/// Layout dos arquivos privados (pacientes, usuários e comprovantes) no storage (#226).
/// Novos uploads: clinicas/{clinicaId}/pacientes/{yyyy/MM}/{guid}{ext}.
/// Legado (sem migração): pacientes/{yyyy/MM}/{guid}{ext}.
/// O prefixo de clínica é defesa em profundidade: o isolamento principal continua sendo o filtro global por ClinicaId.
/// </summary>
public static class PatientFileBlobPath
{
    public const string ClinicSegment = "clinicas";
    public const string PatientSegment = "pacientes";

    public static string Build(int clinicaId, string extension, DateTime utcNow)
    {
        EnsureValidClinicaId(clinicaId);
        return $"{ClinicSegment}/{clinicaId.ToString(CultureInfo.InvariantCulture)}/{BuildLegacy(extension, utcNow)}";
    }

    public static string BuildLegacy(string extension, DateTime utcNow) =>
        $"{PatientSegment}/{utcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";

    public static void EnsureValidClinicaId(int clinicaId)
    {
        if (clinicaId <= 0)
            throw new InvalidOperationException("Clinica invalida para armazenar arquivo.");
    }

    /// <summary>
    /// Clínica dona do nome de blob/caminho relativo JÁ RESOLVIDO pelo storage (sem URL, query ou nova decodificação),
    /// quando ele tem o prefixo clinicas/{id}/pacientes/. Retorna null para caminhos legados (sem prefixo)
    /// e 0 para caminhos malformados ou com segmentos "." e "..", que nunca pertencem a nenhuma clínica.
    /// </summary>
    public static int? GetOwnerClinicaId(string? blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName)) return null;

        var segments = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\\') || segment.Contains('%')))
            return 0;

        for (var index = segments.Length - 1; index >= 2; index--)
        {
            if (segments[index].Equals(PatientSegment, StringComparison.OrdinalIgnoreCase)
                && segments[index - 2].Equals(ClinicSegment, StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(segments[index - 1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                    ? id
                    : 0;
            }
        }

        return null;
    }

    /// <summary>
    /// Fail-closed sobre o nome de blob já resolvido: clínica inválida nunca acessa; prefixo de outra clínica
    /// ou caminho malformado é recusado; legado é aceito (já autorizado pelo registro carregado sob o filtro de tenant).
    /// Deve ser chamado com o mesmo nome que será lido/excluído, nunca com a URL original.
    /// </summary>
    public static bool BelongsTo(string? blobName, int clinicaId)
    {
        if (clinicaId <= 0) return false;
        var owner = GetOwnerClinicaId(blobName);
        return owner is null || owner == clinicaId;
    }
}
