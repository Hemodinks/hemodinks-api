using HemodinksAPI.Application.Storage;

namespace HemodinksAPI.Infrastructure.Storage;

public class FunctionBackedProfilePhotoStorage : IProfilePhotoStorage
{
    private readonly StorageFunctionClient _storageFunctionClient;
    private readonly AzureBlobProfilePhotoStorage _fallbackStorage;

    public FunctionBackedProfilePhotoStorage(
        StorageFunctionClient storageFunctionClient,
        AzureBlobProfilePhotoStorage fallbackStorage)
    {
        _storageFunctionClient = storageFunctionClient;
        _fallbackStorage = fallbackStorage;
    }

    public async Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
    {
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
