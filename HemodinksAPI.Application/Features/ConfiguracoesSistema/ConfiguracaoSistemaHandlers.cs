using HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;
using HemodinksAPI.Application.Features.ConfiguracoesSistema.Queries;
using HemodinksAPI.Application.Storage;
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

public sealed class GetConfiguracaoSistemaPhotoHandler : IRequestHandler<GetConfiguracaoSistemaPhotoQuery, ProfilePhotoFile?>
{
    private readonly IConfiguracaoSistemaRepository _repository;
    private readonly IProfilePhotoStorage _profilePhotoStorage;

    public GetConfiguracaoSistemaPhotoHandler(
        IConfiguracaoSistemaRepository repository,
        IProfilePhotoStorage profilePhotoStorage)
    {
        _repository = repository;
        _profilePhotoStorage = profilePhotoStorage;
    }

    public async Task<ProfilePhotoFile?> Handle(GetConfiguracaoSistemaPhotoQuery request, CancellationToken cancellationToken)
    {
        var configuracao = await _repository.GetCurrentOrCreateAsync(cancellationToken);
        return await _profilePhotoStorage.GetAsync(configuracao.FotoEmpresa, cancellationToken);
    }
}

public sealed class UpdateConfiguracaoSistemaHandler : IRequestHandler<UpdateConfiguracaoSistemaCommand, ConfiguracaoSistemaDto>
{
    private readonly IConfiguracaoSistemaRepository _repository;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly TimeProvider _timeProvider;

    public UpdateConfiguracaoSistemaHandler(
        IConfiguracaoSistemaRepository repository,
        IProfilePhotoStorage profilePhotoStorage,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _profilePhotoStorage = profilePhotoStorage;
        _timeProvider = timeProvider;
    }

    public async Task<ConfiguracaoSistemaDto> Handle(UpdateConfiguracaoSistemaCommand request, CancellationToken cancellationToken)
    {
        var configuracao = await _repository.GetCurrentOrCreateAsync(cancellationToken);
        var fotoEmpresa = await _profilePhotoStorage.SaveAsync(
            request.FotoEmpresa,
            configuracao.FotoEmpresa,
            cancellationToken);

        configuracao.NomeEmpresa = request.NomeEmpresa.Trim();
        configuracao.FotoEmpresa = fotoEmpresa;
        configuracao.DataAtualizacao = _timeProvider.GetUtcNow().UtcDateTime;

        await _repository.SaveChangesAsync(cancellationToken);

        return ConfiguracaoSistemaMapper.ToDto(configuracao);
    }
}
