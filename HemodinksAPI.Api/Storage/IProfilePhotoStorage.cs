namespace HemodinksAPI.Api.Storage;

public interface IProfilePhotoStorage
{
    Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken);

    Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken);
}
