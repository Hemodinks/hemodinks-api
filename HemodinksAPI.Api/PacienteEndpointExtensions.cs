using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Features.Pacientes.Observacoes;
using HemodinksAPI.Application.Features.Pacientes.Queries;
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
            .RequireAuthorization("PacienteCadastrar");

        group.MapPut("/{id}", UpdatePaciente)
            .WithName("UpdatePaciente")
            .WithSummary("Atualizar paciente")
            .RequireAuthorization("PacienteEditar");

        group.MapDelete("/{id}", DeletePaciente)
            .WithName("DeletePaciente")
            .WithSummary("Excluir paciente")
            .RequireAuthorization("Administrador");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadPacienteArquivo")
            .WithSummary("Enviar arquivo do paciente")
            .DisableAntiforgery()
            .RequireAuthorization("PacienteArquivosGerenciar");

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeletePacienteArquivo")
            .WithSummary("Excluir arquivo do paciente")
            .RequireAuthorization("PacienteArquivosGerenciar");

        group.MapGet("/{id}/observacoes", GetObservacoes)
            .WithName("GetPacienteObservacoes")
            .WithSummary("Listar observacoes do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar");

        group.MapPost("/{id}/observacoes", CreateObservacao)
            .WithName("CreatePacienteObservacao")
            .WithSummary("Registrar observacao do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar");

        group.MapPost("/{id}/observacoes/marcar-lidas", MarkObservacoesAsRead)
            .WithName("MarkPacienteObservacoesAsRead")
            .WithSummary("Marcar observacoes do paciente como lidas")
            .RequireAuthorization("PacienteObservacaoGerenciar");
    }

    private static Task<IResult> GetAllPacientes(
        int? page,
        int? pageSize,
        string? search,
        string? medico,
        string? convenio,
        string? procedimento,
        string? sortBy,
        string? sortDirection,
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
                SortBy = sortBy,
                SortDirection = sortDirection,
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

    private static Task<IResult> GetObservacoes(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new GetPacienteObservacoesQuery
            {
                PacienteId = id,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar observacoes do paciente", "Erro ao buscar observacoes do paciente");
    }

    private static Task<IResult> CreateObservacao(
        int id,
        CreatePacienteObservacaoCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.PacienteId = id;
            ApplyCurrentUser(command, GetRequiredCurrentUser(claimsPrincipal));
            return Results.Ok(await mediator.Send(command, cancellationToken));
        }, logger, "Erro ao registrar observacao do paciente", "Erro ao registrar observacao do paciente");
    }

    private static Task<IResult> MarkObservacoesAsRead(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var currentUser = GetRequiredCurrentUser(claimsPrincipal);
            var result = await mediator.Send(new MarkPacienteObservacoesAsReadCommand
            {
                PacienteId = id,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao marcar observacoes do paciente como lidas", "Erro ao atualizar observacoes do paciente");
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

    private static void ApplyCurrentUser(CreatePacienteObservacaoCommand command, CurrentUserContext currentUser)
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
