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
        PublicClinicDirectory directory,
        AppDbContext context,
        ILogger<PublicClinicDirectory> logger,
        CancellationToken cancellationToken)
    {
        var cachedClinics = await directory.TryListAsync(busca, cancellationToken);
        if (cachedClinics != null)
        {
            return Results.Ok(cachedClinics);
        }

        logger.LogWarning(
            "Catalogo JSON publico de clinicas ausente; consultando o banco de dados para o login.");
        var normalizedSearch = busca?.Trim();
        var query = context.Clinicas
            .AsNoTracking()
            .Where(item => item.Ativa);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item =>
                item.Nome.Contains(normalizedSearch)
                || item.Slug.Contains(normalizedSearch));
        }

        var clinics = await query
            .OrderBy(item => item.Nome)
            .Take(50)
            .Select(item => new PublicClinicaResponse(
                item.Id,
                item.Nome,
                item.Slug,
                item.FotoClinica != null && item.FotoClinica != string.Empty
                    ? $"/api/public/clinicas/{item.Slug}/foto"
                    : null))
            .ToListAsync(cancellationToken);

        return Results.Ok(clinics);
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
