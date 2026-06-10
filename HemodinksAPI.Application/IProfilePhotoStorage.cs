namespace HemodinksAPI.Api.Storage;

public interface IProfilePhotoStorage
{
    Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken);

    Task<ProfilePhotoFile?> GetAsync(string? fotoPerfil, CancellationToken cancellationToken);

    Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken);
}

public sealed record ProfilePhotoFile(Stream Content, string ContentType);
