using HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;
using HemodinksAPI.Application.Features.ConfiguracoesSistema.Queries;
using MediatR;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema;

public sealed class GetConfiguracaoSistemaHandler : IRequestHandler<GetConfiguracaoSistemaQuery, ConfiguracaoSistemaDto>
{
    private readonly IConfiguracaoSistemaRepository _repository;

    public GetConfiguracaoSistemaHandler(IConfiguracaoSistemaRepository repository)
    {
        _repository = repository;
    }

    public async Task<ConfiguracaoSistemaDto> Handle(GetConfiguracaoSistemaQuery request, CancellationToken cancellationToken)
    {
        var configuracao = await _repository.GetCurrentOrCreateAsync(cancellationToken);
        return ConfiguracaoSistemaMapper.ToDto(configuracao);
    }
}

public sealed class UpdateConfiguracaoSistemaHandler : IRequestHandler<UpdateConfiguracaoSistemaCommand, ConfiguracaoSistemaDto>
{
    private readonly IConfiguracaoSistemaRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UpdateConfiguracaoSistemaHandler(IConfiguracaoSistemaRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ConfiguracaoSistemaDto> Handle(UpdateConfiguracaoSistemaCommand request, CancellationToken cancellationToken)
    {
        var configuracao = await _repository.GetCurrentOrCreateAsync(cancellationToken);

        configuracao.NomeEmpresa = request.NomeEmpresa.Trim();
        configuracao.DataAtualizacao = _timeProvider.GetUtcNow().UtcDateTime;

        await _repository.SaveChangesAsync(cancellationToken);

        return ConfiguracaoSistemaMapper.ToDto(configuracao);
    }
}
