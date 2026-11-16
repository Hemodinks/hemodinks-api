using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Storage;

public class FunctionBackedProfilePhotoStorage : IProfilePhotoStorage
{
    private readonly StorageFunctionClient _storageFunctionClient;
    private readonly AzureBlobProfilePhotoStorage _fallbackStorage;
    private readonly ProfilePhotoStorageOptions _options;

    public FunctionBackedProfilePhotoStorage(
        StorageFunctionClient storageFunctionClient,
        AzureBlobProfilePhotoStorage fallbackStorage,
        IOptions<ProfilePhotoStorageOptions> options)
    {
        _storageFunctionClient = storageFunctionClient;
        _fallbackStorage = fallbackStorage;
        _options = options.Value;
    }

    public async Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
    {
        // #293: valida antes da Function para foto invalida continuar 400 com mensagem de negocio
        // (erro da Function vira falha interna, 500 sem o corpo da resposta).
        ProfilePhotoStorageSupport.EvaluateRequestedChange(fotoPerfil, currentFotoPerfil, _options.MaxBytes);

        var response = await _storageFunctionClient.PostJsonAsync<RemoteProfilePhotoSaveRequest, RemoteProfilePhotoSaveResponse>(
            "storage/profile-photo",
            new RemoteProfilePhotoSaveRequest(fotoPerfil, currentFotoPerfil),
            cancellationToken);

        return response.FotoPerfil;
    }

    public Task<ProfilePhotoFile?> GetAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        return _fallbackStorage.GetAsync(fotoPerfil, cancellationToken);
    }

    public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
    {
        return _fallbackStorage.DeleteAsync(fotoPerfil, cancellationToken);
    }
}
