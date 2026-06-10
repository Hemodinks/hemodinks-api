using System.Security.Claims;
using HemodinksAPI.Api.Authorization;
using HemodinksAPI.Api.Features.Licencas;
using HemodinksAPI.Api.Features.Pacientes.Commands;
using HemodinksAPI.Api.Features.Pacientes.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static class PacienteEndpointExtensions
{
    public static void MapPacienteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/pacientes")
            .WithTags("Pacientes")
            .RequireAuthorization();

        group.MapGet("/", GetAllPacientes)
            .WithName("GetAllPacientes")
            .WithSummary("Listar pacientes")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar);

        group.MapGet("/{id}", GetPacienteById)
            .WithName("GetPacienteById")
            .WithSummary("Buscar paciente por ID")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar);

        group.MapPost("/", CreatePaciente)
            .WithName("CreatePaciente")
            .WithSummary("Criar paciente")
            .RequireAuthorization(LicencaPolicies.PacientesGerenciar);

        group.MapPut("/{id}", UpdatePaciente)
            .WithName("UpdatePaciente")
            .WithSummary("Atualizar paciente")
            .RequireAuthorization(LicencaPolicies.PacientesGerenciar);

        group.MapDelete("/{id}", DeletePaciente)
            .WithName("DeletePaciente")
            .WithSummary("Excluir paciente");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadPacienteArquivo")
            .WithSummary("Enviar arquivo do paciente")
            .DisableAntiforgery()
            .RequireAuthorization(LicencaPolicies.PacientesGerenciar);

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeletePacienteArquivo")
            .WithSummary("Excluir arquivo do paciente")
            .RequireAuthorization(LicencaPolicies.PacientesGerenciar);
    }

    private static Task<IResult> GetAllPacientes(
        int? page,
        int? pageSize,
        string? search,
        string? medico,
        string? convenio,
        string? procedimento,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetAllPacientesQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                Medico = medico,
                Convenio = convenio,
                Procedimento = procedimento,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar pacientes", "Erro ao buscar pacientes");
    }

    private static Task<IResult> GetPacienteById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(
                new GetPacienteByIdQuery(id, currentUser.Id, currentUser.PerfilId),
                cancellationToken);

            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar paciente", "Erro ao buscar paciente");
    }

    private static Task<IResult> CreatePaciente(
        CreatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            var result = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/pacientes/{result.Id}", result);
        }, logger, "Erro ao criar paciente", "Erro ao criar paciente");
    }

    private static Task<IResult> UpdatePaciente(
        int id,
        UpdatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.Id = id;
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            return Results.Ok(await mediator.Send(command, cancellationToken));
        }, logger, "Erro ao atualizar paciente", "Erro ao atualizar paciente");
    }

    private static Task<IResult> DeletePaciente(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new DeletePacienteCommand
            {
                Id = id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir paciente", "Erro ao excluir paciente");
    }

    private static Task<IResult> UploadArquivo(
        int id,
        IFormFile file,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new UploadPacienteArquivoCommand
            {
                PacienteId = id,
                File = file,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Created($"/api/pacientes/{id}/arquivos/{result.Id}", result);
        }, logger, "Erro ao enviar arquivo do paciente", "Erro ao enviar arquivo");
    }

    private static Task<IResult> DeleteArquivo(
        int id,
        int arquivoId,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            await mediator.Send(new DeletePacienteArquivoCommand
            {
                PacienteId = id,
                ArquivoId = arquivoId,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir arquivo do paciente", "Erro ao excluir arquivo");
    }

    private static void ApplyCurrentUser(CreatePacienteCommand command, CurrentUserContext currentUser)
    {
        command.CurrentUserId = currentUser.Id;
        command.CurrentPerfilId = currentUser.PerfilId;
        command.CurrentUserName = currentUser.Nome;
    }

    private static void ApplyCurrentUser(UpdatePacienteCommand command, CurrentUserContext currentUser)
    {
        command.CurrentUserId = currentUser.Id;
        command.CurrentPerfilId = currentUser.PerfilId;
        command.CurrentUserName = currentUser.Nome;
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
