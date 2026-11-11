using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Le e valida o material de chave da protecao de CPF. Mensagens de erro nunca incluem o valor configurado.
/// </summary>
internal static class CpfProtectionKeyMaterial
{
    public const int MinimumLookupHashKeyBytes = 32;

    public static X509Certificate2 LoadCertificate(CpfProtectionOptions options) =>
        LoadCertificate(options.CertificateBase64, options.CertificatePassword, "CpfProtection:CertificateBase64");

    public static X509Certificate2[] LoadPreviousCertificates(CpfProtectionOptions options) =>
        options.PreviousCertificates
            .Select((item, index) => LoadCertificate(
                item.CertificateBase64,
                item.CertificatePassword,
                $"CpfProtection:PreviousCertificates:{index}:CertificateBase64"))
            .ToArray();

    public static byte[][] LoadPreviousLookupHashKeys(CpfProtectionOptions options) =>
        options.PreviousLookupHashKeys
            .Select((item, index) => LoadLookupHashKey(item, $"CpfProtection:PreviousLookupHashKeys:{index}"))
            .ToArray();

    private static X509Certificate2 LoadCertificate(string? base64, string? password, string setting)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException($"{setting} nao foi configurado.");
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{setting} nao e base64 valido.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(
                raw,
                string.IsNullOrEmpty(password) ? null : password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException($"{setting} nao pode ser lido (PFX ou senha invalidos).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"{setting} deve conter chave privada RSA.");
        }

        return certificate;
    }

    public static byte[] LoadLookupHashKey(CpfProtectionOptions options) =>
        LoadLookupHashKey(options.LookupHashKey, "CpfProtection:LookupHashKey");

    private static byte[] LoadLookupHashKey(string? base64, string setting)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException($"{setting} nao foi configurado.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{setting} nao e base64 valido.");
        }

        if (key.Length < MinimumLookupHashKeyBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"{setting} deve ter no minimo {MinimumLookupHashKeyBytes} bytes.");
        }

        return key;
    }
}
