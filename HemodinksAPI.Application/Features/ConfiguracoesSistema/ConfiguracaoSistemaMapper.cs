using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema;

internal static class ConfiguracaoSistemaMapper
{
    public static ConfiguracaoSistemaDto ToDto(ConfiguracaoSistema configuracao)
    {
        return new ConfiguracaoSistemaDto
        {
            Id = configuracao.Id,
            NomeEmpresa = configuracao.NomeEmpresa,
            DataCadastro = configuracao.DataCadastro,
            DataAtualizacao = configuracao.DataAtualizacao
        };
    }
}
