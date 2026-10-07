using System.Security.Cryptography;
using System.Text;

namespace HemodinksAPI.Application.Security;

public enum PasswordLookupResult { NotFound, Found, Unavailable }

/// <summary>Checks only public password data; never account or clinic data. Must not retain candidates.</summary>
public interface ICompromisedPasswordLookup
{
    PasswordLookupResult Check(string candidate);
}

/// <summary>Used only when defining credentials, never when authenticating or upgrading their hash.</summary>
public sealed class NewPasswordPolicy(ICompromisedPasswordLookup lookup)
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 500;
    private static readonly byte[] RetiredCredentialHash =
        Convert.FromHexString("A2CA37FE6FDC490B8F7CE841E1701A169D2B1697C6B5B5C63F94ABB8F9B6D6DD");

    public static void ValidateLength(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < MinimumLength || candidate.Length > MaximumLength)
            throw new InvalidOperationException("A nova senha deve ter entre 8 e 500 caracteres. Você pode usar uma frase-senha.");
    }

    public void Validate(string? candidate)
    {
        ValidateLength(candidate);
        // This SHA-256 comparison only retires a legacy shared credential. It is not password storage.
        var bytes = Encoding.UTF8.GetBytes(candidate!);
        var digest = SHA256.HashData(bytes);
        bool retired;
        try { retired = CryptographicOperations.FixedTimeEquals(digest, RetiredCredentialHash); }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(digest); }
        if (retired) Reject();
        switch (lookup.Check(candidate!))
        {
            case PasswordLookupResult.NotFound: return;
            case PasswordLookupResult.Found: Reject(); return;
            default: throw new PasswordPolicyUnavailableException();
        }
    }

    private static void Reject() => throw new CompromisedPasswordException();
}

public sealed class CompromisedPasswordException() : InvalidOperationException(
    "Esta senha é comum ou consta na base de senhas comprometidas. Escolha outra senha ou uma frase-senha mais longa.")
{
    public const string Code = "password_compromised";
}

public sealed class PasswordPolicyUnavailableException() : Exception(
    "Não foi possível verificar a nova senha agora. Tente novamente mais tarde; sua senha não foi alterada.");
