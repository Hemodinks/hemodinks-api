using HemodinksAPI.Application.Features.ConfiguracoesSistema;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data.Repositories;

public sealed class ConfiguracaoSistemaRepository : IConfiguracaoSistemaRepository
{
    private readonly AppDbContext _context;
    private readonly IClinicaContext _clinicaContext;
    private readonly TimeProvider _timeProvider;

    public ConfiguracaoSistemaRepository(
        AppDbContext context,
        IClinicaContext clinicaContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _clinicaContext = clinicaContext;
        _timeProvider = timeProvider;
    }

    public async Task<ConfiguracaoSistema> GetCurrentOrCreateAsync(CancellationToken cancellationToken)
    {
        var clinicaId = _clinicaContext.GetRequiredClinicaId();

        var configuracao = await _context.ConfiguracoesSistema
            .FirstOrDefaultAsync(item => item.ClinicaId == clinicaId, cancellationToken);

        if (configuracao != null)
        {
            return configuracao;
        }

        var clinica = await _context.Clinicas
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == clinicaId, cancellationToken)
            ?? throw new KeyNotFoundException("Clinica nao encontrada para a configuracao do sistema.");

        configuracao = new ConfiguracaoSistema
        {
            ClinicaId = clinicaId,
            NomeEmpresa = clinica.Nome,
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
