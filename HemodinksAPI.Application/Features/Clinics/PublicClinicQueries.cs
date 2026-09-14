using HemodinksAPI.Application.Storage;

namespace HemodinksAPI.Application.Features.Clinics;

public sealed class PublicClinicQueries(
    IPublicClinicDirectory directory,
    IProfilePhotoStorage storage)
{
    public async Task<List<PublicClinicResponse>> ListActiveAsync(string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim();
        var clinics = await directory.ListActiveAsync(normalizedSearch, cancellationToken);
        return clinics
            .Select(item => new PublicClinicResponse(item.Id, item.Nome, item.Slug,
                item.HasPhoto
                    ? $"/api/public/clinicas/{item.Slug}/foto"
                    : null))
            .ToList();
    }

    public async Task<ProfilePhotoFile?> GetPhotoAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var photo = await directory.FindActivePhotoReferenceAsync(normalizedSlug, cancellationToken);
        return string.IsNullOrEmpty(photo) ? null : await storage.GetAsync(photo, cancellationToken);
    }
}

public sealed record PublicClinicResponse(int Id, string Nome, string Slug, string? FotoUrl);
