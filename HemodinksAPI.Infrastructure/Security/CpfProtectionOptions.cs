namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Configuracao da protecao de CPF em repouso (#227). Valores vem de secrets do ambiente
/// (Container Apps, Render, user-secrets); nunca de appsettings versionado nem de log.
/// </summary>
public sealed class CpfProtectionOptions
{
    public const string SectionName = "CpfProtection";

    /// <summary>PFX em base64 com chave privada; protege o anel de chaves do Data Protection.</summary>
    public string? CertificateBase64 { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Chave do HMAC-SHA256 em base64, com no minimo 32 bytes.</summary>
    public string? LookupHashKey { get; set; }

    /// <summary>Reconcilia CpfCifrado/CpfHash com a coluna legada no startup. Padrao: ligado fora de Testing.</summary>
    public bool? ReconcileOnStartup { get; set; }

    internal bool HasCertificate => !string.IsNullOrWhiteSpace(CertificateBase64);

    internal bool HasLookupHashKey => !string.IsNullOrWhiteSpace(LookupHashKey);
}
