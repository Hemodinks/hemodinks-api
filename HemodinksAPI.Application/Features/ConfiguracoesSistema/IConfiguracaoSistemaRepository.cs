using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema;

public interface IConfiguracaoSistemaRepository
{
    Task<ConfiguracaoSistema> GetCurrentOrCreateAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
