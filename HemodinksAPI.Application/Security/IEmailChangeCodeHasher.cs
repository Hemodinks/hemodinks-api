namespace HemodinksAPI.Application.Security;

/// <summary>
/// Hash com chave do servidor para o código curto da alteração de email (#322). Com 6 dígitos, um hash
/// sem chave seria revertido na hora se a tabela vazasse; o HMAC exige também a chave.
/// </summary>
public interface IEmailChangeCodeHasher
{
    /// <summary>Hash hexadecimal (64 caracteres) do código, ligado à solicitação.</summary>
    string Hash(Guid requestId, string code);
}
