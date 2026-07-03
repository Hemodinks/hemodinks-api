using HemodinksAPI.Application.Features.ConfiguracoesSistema;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data.Repositories;

public sealed class ConfiguracaoSistemaRepository : IConfiguracaoSistemaRepository
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ConfiguracaoSistemaRepository(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<ConfiguracaoSistema> GetCurrentOrCreateAsync(CancellationToken cancellationToken)
    {
        var configuracao = await _context.ConfiguracoesSistema
            .FirstOrDefaultAsync(item => item.Id == ConfiguracaoSistema.DefaultId, cancellationToken);

        if (configuracao != null)
        {
            return configuracao;
        }

        configuracao = new ConfiguracaoSistema
        {
            Id = ConfiguracaoSistema.DefaultId,
            ClinicaId = Clinica.DefaultId,
            NomeEmpresa = ConfiguracaoSistema.DefaultNomeEmpresa,
            DataCadastro = _timeProvider.GetUtcNow().UtcDateTime
        };

        _context.ConfiguracoesSistema.Add(configuracao);
        await _context.SaveChangesAsync(cancellationToken);

        return configuracao;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
