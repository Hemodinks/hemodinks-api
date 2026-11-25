using HemodinksAPI.Application.Features.Auditoria;
using MediatR;

namespace HemodinksAPI.Api;

public static class AuditoriaClinicaEndpointExtensions
{
    // #368: trilha de acesso a pacientes, so Administrador/SuperAdministrador e sempre restrita a clinica
    // ativa (filtro de tenant). Tela geral: filtros da trilha e os mesmos da tela de Pacientes - Cirurgias.
    public static void MapAuditoriaClinicaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auditoria-clinica", async (int? page, int? pageSize, string? recurso, string? recursoId,
                [AsParameters] AuditoriaClinicaFiltros filtros, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ListarAuditoriaClinicaQuery(page ?? 1, pageSize ?? 20, recurso, recursoId, filtros), ct)))
            .WithTags("Auditoria clinica")
            .WithName("ListarAuditoriaClinica")
            .WithSummary("Listar trilha de auditoria de pacientes da clinica")
            .RequireAuthorization("Administrador");

        app.MapGet("/api/auditoria-clinica/configuracao", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ObterAuditoriaConfiguracaoQuery(), ct)))
            .WithTags("Auditoria clinica")
            .WithName("ObterAuditoriaConfiguracao")
            .WithSummary("Consultar se a clinica audita alteracoes de pacientes e atendimentos")
            .RequireAuthorization("Administrador");

        app.MapPut("/api/auditoria-clinica/configuracao", async (AtualizarAuditoriaConfiguracaoCommand command,
                IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(command, ct)))
            .WithTags("Auditoria clinica")
            .WithName("AtualizarAuditoriaConfiguracao")
            .WithSummary("Ligar ou desligar a auditoria de alteracoes de pacientes e atendimentos")
            .RequireAuthorization("Administrador");
    }
}
