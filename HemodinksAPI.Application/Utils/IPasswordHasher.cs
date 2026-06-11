namespace HemodinksAPI.Application.Utils;

/// <summary>
/// Servico para hash e verificacao de senhas.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string hash);
}
