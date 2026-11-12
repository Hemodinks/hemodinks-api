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

    /// <summary>
    /// Hashes aceitos na comparacao: chave atual primeiro e depois as anteriores, enquanto uma rotacao
    /// da chave de hash nao termina. Vazio quando nao ha digitos.
    /// </summary>
    string[] ComputeLookupHashCandidates(string? cpf);

    /// <summary>
    /// true quando a cifra foi feita com uma chave do Data Protection protegida por um certificado
    /// anterior; o reconciliador recifra essas linhas para que o certificado antigo possa ser removido.
    /// </summary>
    bool RequiresReprotection(string protectedCpf);
}
