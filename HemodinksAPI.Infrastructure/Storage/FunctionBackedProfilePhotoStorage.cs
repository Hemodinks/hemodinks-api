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
        // #293: valida antes da Function para foto invalida continuar 400 com mensagem de negocio.
        // Se a Function recusar por regra (ex.: limite configurado menor que o da API), ela devolve 400
        // com a mensagem e o StorageFunctionClient a repassa como BusinessRuleException.
        var change = ProfilePhotoStorageSupport.EvaluateRequestedChange(fotoPerfil, currentFotoPerfil, _options.MaxBytes);

        // #399: sem nada a gravar ou apagar, a Function so devolveria o mesmo resultado do armazenamento direto
        // (AzureBlobProfilePhotoStorage.SaveAsync). Nao chama-la tira criar/editar paciente sem foto do cold start dela.
        if (change.Kind == ProfilePhotoChangeKind.KeepCurrent)
        {
            return currentFotoPerfil;
        }

        if (change.Kind == ProfilePhotoChangeKind.RemoveCurrent
            && (string.IsNullOrWhiteSpace(currentFotoPerfil) || ProfilePhotoStorageSupport.IsDataUrl(currentFotoPerfil)))
        {
            return null;
        }

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
