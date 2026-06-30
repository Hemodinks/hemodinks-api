using HemodinksAPI.Application.Storage;
using MediatR;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema.Queries;

public sealed class GetConfiguracaoSistemaQuery : IRequest<ConfiguracaoSistemaDto>
{
}

public sealed class GetConfiguracaoSistemaPhotoQuery : IRequest<ProfilePhotoFile?>
{
}
