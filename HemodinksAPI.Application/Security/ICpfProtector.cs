namespace HemodinksAPI.Application.Security;

/// <summary>
/// Protege o CPF em repouso (#227): cifra reversivel via ASP.NET Data Protection e
/// hash deterministico com chave (HMAC-SHA256) para unicidade e busca exata.
/// Nunca registre em log o valor recebido nem o retornado.
/// </summary>
public interface ICpfProtector
{
    string Protect(string cpf);

    /// <summary>Retorna null quando a cifra nao pode ser lida (chave ausente ou payload invalido).</summary>
    string? TryUnprotect(string protectedCpf);

    /// <summary>Hash de busca do CPF normalizado (somente digitos); null quando nao ha digitos.</summary>
    string? ComputeLookupHash(string? cpf);
}
