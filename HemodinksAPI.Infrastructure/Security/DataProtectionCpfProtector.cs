using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Cifra o CPF com ASP.NET Data Protection (proposito dedicado) e gera o hash de busca com HMAC-SHA256.
/// Nenhuma primitiva criptografica propria: apenas APIs do framework.
/// </summary>
public sealed class DataProtectionCpfProtector : ICpfProtector, IDisposable
{
    internal const string Purpose = "HemodinksAPI.User.Cpf.v1";

    private readonly object _rebuildLock = new();
    private readonly List<IDisposable> _ownedResources = [];
    private readonly byte[] _lookupHashKey;
    private readonly byte[][] _previousLookupHashKeys;
    private readonly CpfCertificateRotation? _certificateRotation;
    private readonly Func<(IDataProtectionProvider Provider, IDisposable Owner)>? _rebuildProvider;
    private volatile IDataProtector _protector;

    public DataProtectionCpfProtector(
        IDataProtectionProvider provider,
        byte[] lookupHashKey,
        IDisposable? ownedResources = null,
        IReadOnlyList<byte[]>? previousLookupHashKeys = null)
        : this(provider, lookupHashKey, ownedResources, previousLookupHashKeys, null, null)
    {
    }

    internal DataProtectionCpfProtector(
        IDataProtectionProvider provider,
        byte[] lookupHashKey,
        IDisposable? ownedResources,
        IReadOnlyList<byte[]>? previousLookupHashKeys,
        CpfCertificateRotation? certificateRotation,
        Func<(IDataProtectionProvider Provider, IDisposable Owner)>? rebuildProvider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (lookupHashKey is null || lookupHashKey.Length < CpfProtectionKeyMaterial.MinimumLookupHashKeyBytes)
        {
            throw new ArgumentException("Chave de hash do CPF insuficiente.", nameof(lookupHashKey));
        }

        _protector = provider.CreateProtector(Purpose);
        _lookupHashKey = lookupHashKey.ToArray();
        _previousLookupHashKeys = (previousLookupHashKeys ?? [])
            .Select(key => key is { Length: >= CpfProtectionKeyMaterial.MinimumLookupHashKeyBytes }
                ? key.ToArray()
                : throw new ArgumentException("Chave de hash anterior do CPF insuficiente.", nameof(previousLookupHashKeys)))
            .ToArray();
        if (ownedResources != null)
        {
            _ownedResources.Add(ownedResources);
        }

        _certificateRotation = certificateRotation;
        _rebuildProvider = rebuildProvider;
    }

    /// <summary>
    /// Rotacao de certificado; sem efeito no provider efemero. Quando cria uma chave nova, troca o
    /// provider: o atual so relê o anel de chaves em segundo plano e continuaria cifrando com a antiga.
    /// </summary>
    public CpfCertificateRotationResult PrepareCertificateRotation()
    {
        if (_certificateRotation is null)
        {
            return new CpfCertificateRotationResult(0, false);
        }

        var result = _certificateRotation.Prepare(_protector);
        if (result.CreatedKey && _rebuildProvider != null)
        {
            var (provider, owner) = _rebuildProvider();
            lock (_rebuildLock)
            {
                // O provider anterior nao e descartado agora: pode haver uma operacao em andamento nele.
                _ownedResources.Add(owner);
                _protector = provider.CreateProtector(Purpose);
            }
        }

        return result;
    }

    public bool RequiresReprotection(string protectedCpf) =>
        _certificateRotation?.IsRetiring(protectedCpf) == true;

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

        return Hash(_lookupHashKey, digits);
    }

    public string[] ComputeLookupHashCandidates(string? cpf)
    {
        var digits = CpfUtils.Normalize(cpf);
        if (string.IsNullOrEmpty(digits))
        {
            return [];
        }

        return [Hash(_lookupHashKey, digits), .. _previousLookupHashKeys.Select(key => Hash(key, digits))];
    }

    private static string Hash(byte[] key, string digits) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(digits)));

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_lookupHashKey);
        foreach (var key in _previousLookupHashKeys)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        lock (_rebuildLock)
        {
            foreach (var owned in _ownedResources)
            {
                owned.Dispose();
            }
        }
    }
}
