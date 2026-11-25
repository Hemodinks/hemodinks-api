using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Auditing;

/// <summary>
/// Estado da auditoria da requisicao atual (scoped). O handler informa o paciente afetado e o detalhe
/// (motivo ou antes/depois); o filtro de endpoint grava a entrada no fim da requisicao.
/// </summary>
public interface IClinicAuditRecorder
{
    int? PacienteId { get; }
    string? Detalhe { get; }
    // Endpoints de atendimento so geram entrada quando o handler confirma que a auditoria esta ligada.
    bool Requested { get; }

    void ForPaciente(int pacienteId);
    void SetDetalhe(ClinicAuditDetail detalhe);
    void Request();
    // Opcao da clinica (Opcoes -> Configuracoes); sempre sob o filtro de tenant da requisicao.
    Task<bool> AuditarAtendimentosAsync(CancellationToken cancellationToken);
}

public sealed class ClinicAuditRecorder(IClinicAuditDbContext db) : IClinicAuditRecorder
{
    private bool? _auditarAtendimentos;

    public int? PacienteId { get; private set; }
    public string? Detalhe { get; private set; }
    public bool Requested { get; private set; }

    public void ForPaciente(int pacienteId) => PacienteId = pacienteId;
    public void SetDetalhe(ClinicAuditDetail detalhe) => Detalhe = detalhe.ToJson();
    public void Request() => Requested = true;

    public async Task<bool> AuditarAtendimentosAsync(CancellationToken cancellationToken) =>
        _auditarAtendimentos ??= await db.ConfiguracoesSistema.AsNoTracking()
            .AnyAsync(item => item.AuditarAtendimentos, cancellationToken);
}

public sealed record ClinicAuditChange(
    [property: JsonPropertyName("campo")] string Campo,
    [property: JsonPropertyName("de")] string? De,
    [property: JsonPropertyName("para")] string? Para,
    [property: JsonPropertyName("textoAlterado"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool TextoAlterado = false);

/// <summary>
/// Detalhe gravado na trilha. Campos estruturados guardam antes/depois; texto livre (diagnostico,
/// tratamento, observacoes, nome) so indica que mudou. Limitado a 4000 caracteres (coluna).
/// </summary>
public sealed class ClinicAuditDetail
{
    public const int MaxMotivoLength = 500;
    private const int MaxLength = 4000;
    private const int MaxValueLength = 120;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly List<ClinicAuditChange> _alteracoes = [];

    [JsonPropertyName("motivo")]
    public string? Motivo { get; private init; }

    [JsonPropertyName("alteracoes")]
    public IReadOnlyList<ClinicAuditChange>? Alteracoes => _alteracoes.Count > 0 ? _alteracoes : null;

    [JsonIgnore]
    public bool HasChanges => _alteracoes.Count > 0;

    public static ClinicAuditDetail ComMotivo(string motivo) => new() { Motivo = motivo };

    public ClinicAuditDetail Campo(string campo, object? antes, object? depois)
    {
        var de = Format(antes);
        var para = Format(depois);
        if (!string.Equals(de, para, StringComparison.Ordinal))
            _alteracoes.Add(new ClinicAuditChange(campo, de, para));
        return this;
    }

    // Texto livre: so registra que mudou, nunca o conteudo.
    public ClinicAuditDetail Texto(string campo, string? antes, string? depois)
    {
        if (!string.Equals(Normalize(antes), Normalize(depois), StringComparison.Ordinal))
            _alteracoes.Add(new ClinicAuditChange(campo, null, null, TextoAlterado: true));
        return this;
    }

    public string ToJson()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        if (json.Length <= MaxLength)
            return json;

        // Muitas alteracoes: mantem as primeiras e termina com um marcador de corte, sem quebrar o JSON.
        var marker = new ClinicAuditChange("...", null, null, TextoAlterado: true);
        do
        {
            _alteracoes.Remove(marker);
            if (_alteracoes.Count > 0)
                _alteracoes.RemoveAt(_alteracoes.Count - 1);
            _alteracoes.Add(marker);
            json = JsonSerializer.Serialize(this, JsonOptions);
        } while (json.Length > MaxLength && _alteracoes.Count > 1);
        return json;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Format(object? value)
    {
        var text = value switch
        {
            null => null,
            bool flag => flag ? "Sim" : "Nao",
            DateTime date => date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.00", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        text = Normalize(text);
        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] + "..." : text;
    }
}
