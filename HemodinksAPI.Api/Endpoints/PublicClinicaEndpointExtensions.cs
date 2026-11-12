using HemodinksAPI.Application.Features.Clinics;

namespace HemodinksAPI.Api;

public static class PublicClinicaEndpointExtensions
{
    public static void MapPublicClinicaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/public/clinicas")
            .WithTags("Clinicas - Publico")
            .AllowAnonymous()
            .RequireRateLimiting("PublicClinics");

        group.MapGet("/", ListActiveClinicas);
        group.MapGet("/{slug}/foto", GetClinicPhoto);
    }

    private static async Task<IResult> ListActiveClinicas(
        string? busca,
        PublicClinicQueries queries,
        CancellationToken cancellationToken)
    {
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
