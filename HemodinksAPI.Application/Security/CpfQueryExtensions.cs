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
        var cpfHash = context.ComputeCpfLookupHash(normalizedCpf);
        return users.Where(user => user.CpfHash == cpfHash || user.Cpf == normalizedCpf);
    }
}
