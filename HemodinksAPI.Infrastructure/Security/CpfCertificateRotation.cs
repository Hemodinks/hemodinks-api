using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Rotacao do certificado que protege o anel de chaves do CPF (#227). Uma chave do Data Protection
/// fica presa ao certificado que a cifrou: para aposentar o certificado anterior e preciso (1) ter uma
/// chave padrao cifrada pelo certificado atual e (2) recifrar os CPFs que usam chaves antigas.
/// Nao ha criptografia propria: so leitura do cabecalho documentado do payload e do XML do anel.
/// </summary>
internal sealed class CpfCertificateRotation(
    IXmlRepository repository,
    IKeyManager keyManager,
    X509Certificate2 currentCertificate)
{
    // Cabecalho do payload do Data Protection: 4 bytes de magic header + 16 bytes do id da chave.
    private const int MagicHeaderLength = 4;
    private const int KeyIdLength = 16;
    private static readonly TimeSpan NewKeyLifetime = TimeSpan.FromDays(90);

    private volatile HashSet<Guid> _retiringKeyIds = [];

    /// <summary>
    /// Identifica as chaves cifradas por outro certificado e, se a chave padrao for uma delas, cria
    /// uma chave nova (cifrada pelo certificado atual). Idempotente; seguro em varias replicas
    /// (no pior caso cada uma cria uma chave, o que e inofensivo).
    /// </summary>
    public CpfCertificateRotationResult Prepare(IDataProtector protector)
    {
        var retiring = FindKeysProtectedByOtherCertificates();
        _retiringKeyIds = retiring;

        var createdKey = false;
        if (retiring.Count > 0 && TryReadKeyId(protector.Protect("rotation-probe"), out var defaultKeyId)
            && retiring.Contains(defaultKeyId))
        {
            var now = DateTimeOffset.UtcNow;
            keyManager.CreateNewKey(now, now.Add(NewKeyLifetime));
            createdKey = true;
        }

        return new CpfCertificateRotationResult(retiring.Count, createdKey);
    }

    public bool IsRetiring(string protectedValue) =>
        _retiringKeyIds.Count > 0
        && TryReadKeyId(protectedValue, out var keyId)
        && _retiringKeyIds.Contains(keyId);

    internal static bool TryReadKeyId(string protectedValue, out Guid keyId)
    {
        keyId = Guid.Empty;
        byte[] payload;
        try
        {
            payload = Base64UrlDecode(protectedValue);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length < MagicHeaderLength + KeyIdLength)
        {
            return false;
        }

        keyId = new Guid(payload.AsSpan(MagicHeaderLength, KeyIdLength));
        return true;
    }

    private HashSet<Guid> FindKeysProtectedByOtherCertificates()
    {
        var retiring = new HashSet<Guid>();
        foreach (var element in repository.GetAllElements())
        {
            if (element.Name.LocalName != "key"
                || !Guid.TryParse((string?)element.Attribute("id"), out var keyId))
            {
                continue;
            }

            var thumbprint = ReadEncryptingCertificateThumbprint(element);
            // Sem certificado identificavel a chave nao e marcada: evita criar chave nova a cada startup.
            if (thumbprint != null
                && !string.Equals(thumbprint, currentCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                retiring.Add(keyId);
            }
        }

        return retiring;
    }

    private static string? ReadEncryptingCertificateThumbprint(System.Xml.Linq.XElement keyElement)
    {
        var encoded = keyElement.Descendants().FirstOrDefault(item => item.Name.LocalName == "X509Certificate")?.Value;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(encoded.Trim()));
            return certificate.Thumbprint;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            0 => base64,
            _ => throw new FormatException()
        };
        return Convert.FromBase64String(base64);
    }
}

public sealed record CpfCertificateRotationResult(int RetiringKeys, bool CreatedKey);
