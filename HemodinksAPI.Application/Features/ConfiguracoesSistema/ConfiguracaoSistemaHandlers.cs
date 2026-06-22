using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;
using HemodinksAPI.Application.Features.ConfiguracoesSistema.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema;

public sealed class ConfiguracaoSistemaHandler :
    IRequestHandler<GetConfiguracaoSistemaQuery, ConfiguracaoSistemaDto>,
    IRequestHandler<UpdateConfiguracaoSistemaCommand, ConfiguracaoSistemaDto>
{
    private readonly IAppDbContext _context;

    public ConfiguracaoSistemaHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<ConfiguracaoSistemaDto> Handle(GetConfiguracaoSistemaQuery request, CancellationToken cancellationToken)
    {
        var configuracao = await GetOrCreateAsync(cancellationToken);
        return ConfiguracaoSistemaMapper.ToDto(configuracao);
    }

    public async Task<ConfiguracaoSistemaDto> Handle(UpdateConfiguracaoSistemaCommand request, CancellationToken cancellationToken)
    {
        var configuracao = await GetOrCreateAsync(cancellationToken);

        configuracao.NomeEmpresa = request.NomeEmpresa.Trim();
        configuracao.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ConfiguracaoSistemaMapper.ToDto(configuracao);
    }

    private async Task<ConfiguracaoSistema> GetOrCreateAsync(CancellationToken cancellationToken)
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
            NomeEmpresa = "Hemodinks",
            DataCadastro = DateTime.UtcNow
        };

        _context.ConfiguracoesSistema.Add(configuracao);
        await _context.SaveChangesAsync(cancellationToken);

        return configuracao;
    }
}
