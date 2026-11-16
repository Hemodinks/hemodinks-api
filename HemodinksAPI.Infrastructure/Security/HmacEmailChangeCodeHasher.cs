using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Security;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 do código da alteração de email (#322). A chave é derivada por HKDF da chave de hash do
/// CPF com rótulo próprio: não exige segredo novo e fica criptograficamente independente da chave do CPF.
/// Rotacionar a chave do CPF só invalida códigos pendentes (validade de minutos).
/// </summary>
public sealed class HmacEmailChangeCodeHasher : IEmailChangeCodeHasher, IDisposable
{
    internal static readonly byte[] DerivationInfo = Encoding.UTF8.GetBytes("HemodinksAPI.EmailChangeCode.v1");
    private readonly byte[] _key;

    private HmacEmailChangeCodeHasher(byte[] key) => _key = key;

    public static HmacEmailChangeCodeHasher FromLookupHashKey(byte[] lookupHashKey)
    {
        if (lookupHashKey is null || lookupHashKey.Length < CpfProtectionKeyMaterial.MinimumLookupHashKeyBytes)
            throw new ArgumentException("Chave insuficiente para o código de alteração de email.", nameof(lookupHashKey));
        return new(HKDF.DeriveKey(HashAlgorithmName.SHA256, lookupHashKey, 32, salt: null, info: DerivationInfo));
    }

    /// <summary>Apenas Development/Testing: chave em memória, perdida ao reiniciar.</summary>
    public static HmacEmailChangeCodeHasher CreateEphemeral() => new(RandomNumberGenerator.GetBytes(32));

    public string Hash(Guid requestId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{requestId:N}:{code}")));

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
