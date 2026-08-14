using HemodinksAPI.Application.Features.Clinics;

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
        PublicClinicQueries queries,
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
        return Results.Ok(await queries.ListActiveAsync(busca, cancellationToken));
    }

    private static async Task<IResult> GetClinicPhoto(
        string slug,
        PublicClinicQueries queries,
        CancellationToken cancellationToken)
    {
        var file = await queries.GetPhotoAsync(slug, cancellationToken);

        return file == null
            ? Results.NotFound()
            : Results.Stream(file.Content, file.ContentType);
    }
}
