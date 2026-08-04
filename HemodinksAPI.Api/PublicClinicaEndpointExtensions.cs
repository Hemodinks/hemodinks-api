using HemodinksAPI.Application.Storage;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static class PublicClinicaEndpointExtensions
{
    public static void MapPublicClinicaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/public/clinicas")
            .WithTags("Clinicas - Publico")
            .AllowAnonymous()
            .RequireRateLimiting("PublicClinicDirectory");

        group.MapGet("/", ListActiveClinicas);
        group.MapGet("/{slug}/foto", GetClinicPhoto);
    }

    private static async Task<IResult> ListActiveClinicas(
        string? busca,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = busca?.Trim().ToLowerInvariant();
        var query = context.Clinicas.AsNoTracking().Where(item => item.Ativa);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item => item.Nome.ToLower().Contains(normalizedSearch)
                || item.Slug.Contains(normalizedSearch));
        }

        var items = await query
            .OrderBy(item => item.Nome)
            .Take(50)
            .Select(item => new PublicClinicaResponse(
                item.Id,
                item.Nome,
                item.Slug,
                item.FotoClinica != null ? $"/api/public/clinicas/{item.Slug}/foto" : null))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetClinicPhoto(
        string slug,
        AppDbContext context,
        IProfilePhotoStorage storage,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var photo = await context.Clinicas
            .AsNoTracking()
            .Where(item => item.Ativa && item.Slug == normalizedSlug)
            .Select(item => item.FotoClinica)
            .FirstOrDefaultAsync(cancellationToken);
        var file = await storage.GetAsync(photo, cancellationToken);

        return file == null
            ? Results.NotFound()
            : Results.Stream(file.Content, file.ContentType);
    }

    public sealed record PublicClinicaResponse(int Id, string Nome, string Slug, string? FotoUrl);
}
