namespace HemodinksAPI.Application.Features.Clinics;

/// <summary>Read-only access to the public directory, restricted to active clinics.</summary>
public interface IPublicClinicDirectory
{
    Task<List<PublicClinicSummary>> ListActiveAsync(string? search, CancellationToken cancellationToken);
    Task<string?> FindActivePhotoReferenceAsync(string slug, CancellationToken cancellationToken);
}

public sealed record PublicClinicSummary(int Id, string Nome, string Slug, bool HasPhoto);
