using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Domain.Utils;
using Microsoft.AspNetCore.DataProtection;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Cifra o CPF com ASP.NET Data Protection (proposito dedicado) e gera o hash de busca com HMAC-SHA256.
/// Nenhuma primitiva criptografica propria: apenas APIs do framework.
/// </summary>
public sealed class DataProtectionCpfProtector : ICpfProtector, IDisposable
{
    internal const string Purpose = "HemodinksAPI.User.Cpf.v1";

    private readonly IDataProtector _protector;
    private readonly byte[] _lookupHashKey;
    private readonly IDisposable? _ownedResources;

    public DataProtectionCpfProtector(
        IDataProtectionProvider provider,
        byte[] lookupHashKey,
        IDisposable? ownedResources = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (lookupHashKey is null || lookupHashKey.Length < CpfProtectionKeyMaterial.MinimumLookupHashKeyBytes)
        {
            throw new ArgumentException("Chave de hash do CPF insuficiente.", nameof(lookupHashKey));
        }

        _protector = provider.CreateProtector(Purpose);
        _lookupHashKey = lookupHashKey.ToArray();
        _ownedResources = ownedResources;
    }

    public string Protect(string cpf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);
        return _protector.Protect(cpf);
    }

    public string? TryUnprotect(string protectedCpf)
    {
        if (string.IsNullOrWhiteSpace(protectedCpf))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedCpf);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public string? ComputeLookupHash(string? cpf)
    {
        var digits = CpfUtils.Normalize(cpf);
        if (string.IsNullOrEmpty(digits))
        {
            return null;
        }

        return Convert.ToHexString(HMACSHA256.HashData(_lookupHashKey, Encoding.UTF8.GetBytes(digits)));
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_lookupHashKey);
        _ownedResources?.Dispose();
    }
}
