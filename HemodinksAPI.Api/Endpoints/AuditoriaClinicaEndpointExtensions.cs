using HemodinksAPI.Application.Features.Auditoria;
using MediatR;

namespace HemodinksAPI.Api;

public static class AuditoriaClinicaEndpointExtensions
{
    // #368: trilha de acesso a pacientes, so Administrador e sempre restrita a clinica ativa (filtro de tenant).
    public static void MapAuditoriaClinicaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auditoria-clinica", async (int? page, int? pageSize, string? recurso, string? recursoId,
                IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ListarAuditoriaClinicaQuery(page ?? 1, pageSize ?? 20, recurso, recursoId), ct)))
            .WithTags("Auditoria clinica")
            .WithName("ListarAuditoriaClinica")
            .WithSummary("Listar trilha de auditoria de pacientes da clinica")
            .RequireAuthorization("Administrador");
    }
}
