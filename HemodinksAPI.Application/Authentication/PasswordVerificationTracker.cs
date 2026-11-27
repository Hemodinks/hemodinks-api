using HemodinksAPI.Application.Utils;

namespace HemodinksAPI.Application.Authentication;

/// <summary>
/// #432: envolve o hasher de um unico request para saber se alguma senha foi verificada antes de uma falha.
/// Nao guarda senha, hash nem resultado; so conta as verificacoes.
/// </summary>
internal sealed class PasswordVerificationTracker(IPasswordHasher inner) : IPasswordHasher
{
    public int Verifications { get; private set; }

    public string HashPassword(string password) => inner.HashPassword(password);

    public bool VerifyPassword(string password, string hash)
    {
        Verifications++;
        return inner.VerifyPassword(password, hash);
    }

    public PasswordVerificationResult VerifyPasswordWithRehash(string password, string hash)
    {
        Verifications++;
        return inner.VerifyPasswordWithRehash(password, hash);
    }

    /// <summary>Falha de login sem verificacao paga o mesmo custo de uma senha errada.</summary>
    public void EqualizeIfNothingVerified(string? password)
    {
        if (Verifications == 0)
        {
            PasswordTimingEqualizer.VerifyAgainstReference(inner, password);
        }
    }
}
