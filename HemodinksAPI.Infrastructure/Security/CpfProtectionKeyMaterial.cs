using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Le e valida o material de chave da protecao de CPF. Mensagens de erro nunca incluem o valor configurado.
/// </summary>
internal static class CpfProtectionKeyMaterial
{
    public const int MinimumLookupHashKeyBytes = 32;

    public static X509Certificate2 LoadCertificate(CpfProtectionOptions options)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(options.CertificateBase64!.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("CpfProtection:CertificateBase64 nao e base64 valido.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(
                raw,
                string.IsNullOrEmpty(options.CertificatePassword) ? null : options.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("CpfProtection:CertificateBase64 nao pode ser lido (PFX ou senha invalidos).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException("CpfProtection:CertificateBase64 deve conter chave privada RSA.");
        }

        return certificate;
    }

    public static byte[] LoadLookupHashKey(CpfProtectionOptions options)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.LookupHashKey!.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("CpfProtection:LookupHashKey nao e base64 valido.");
        }

        if (key.Length < MinimumLookupHashKeyBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"CpfProtection:LookupHashKey deve ter no minimo {MinimumLookupHashKeyBytes} bytes.");
        }

        return key;
    }
}
