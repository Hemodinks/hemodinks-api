using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics;

public sealed class PublicClinicQueries(
    IClinicDirectoryDbContext context,
    IProfilePhotoStorage storage)
{
    public Task<List<PublicClinicResponse>> ListActiveAsync(string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim();
        var query = context.Clinicas.AsNoTracking().Where(item => item.Ativa);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item => item.Nome.Contains(normalizedSearch) || item.Slug.Contains(normalizedSearch));
        }

        return query.OrderBy(item => item.Nome).Take(50)
            .Select(item => new PublicClinicResponse(item.Id, item.Nome, item.Slug,
                item.FotoClinica != null && item.FotoClinica != string.Empty
                    ? $"/api/public/clinicas/{item.Slug}/foto"
                    : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfilePhotoFile?> GetPhotoAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var photo = await context.Clinicas.AsNoTracking()
            .Where(item => item.Ativa && item.Slug == normalizedSlug)
            .Select(item => item.FotoClinica)
            .FirstOrDefaultAsync(cancellationToken);

        return await storage.GetAsync(photo, cancellationToken);
    }
}

public sealed record PublicClinicResponse(int Id, string Nome, string Slug, string? FotoUrl);
