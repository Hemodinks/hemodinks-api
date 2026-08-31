using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

internal static class FinanceiroManagementQueries
{
    public static IQueryable<AtendimentoCirurgico> FullAtendimento(IQueryable<AtendimentoCirurgico> query) =>
        query.Include(x => x.Paciente).Include(x => x.OpmeFornecedor).Include(x => x.Procedimentos)
            .Include(x => x.Arquivos);

    public static IQueryable<ContaReceber> FullConta(IQueryable<ContaReceber> query) =>
        query.Include(x => x.Paciente).Include(x => x.Recebimentos);

    public static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new InvalidOperationException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
    }
}


