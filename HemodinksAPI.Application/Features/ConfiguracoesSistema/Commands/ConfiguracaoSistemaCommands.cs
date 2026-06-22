using MediatR;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;

public sealed class UpdateConfiguracaoSistemaCommand : IRequest<ConfiguracaoSistemaDto>
{
    public string NomeEmpresa { get; set; } = string.Empty;
}
