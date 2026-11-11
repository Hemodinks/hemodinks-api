using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Security;

public static class CpfQueryExtensions
{
    /// <summary>
    /// Filtro de unicidade de CPF. Fase 1 do #227: a coluna legada ainda e a fonte da verdade e linhas
    /// gravadas por uma imagem anterior (rollback ou janela blue/green) podem estar sem hash, por isso a
    /// comparacao em texto continua aqui. Unico ponto autorizado pelo teste de arquitetura; remover na fase 2.
    /// </summary>
    public static IQueryable<User> WhereCpfMatches(this IQueryable<User> users, IUserDbContext context, string normalizedCpf)
    {
        var cpfHashes = context.ComputeCpfLookupHashCandidates(normalizedCpf);
        return users.Where(user => (user.CpfHash != null && cpfHashes.Contains(user.CpfHash)) || user.Cpf == normalizedCpf);
    }
}
