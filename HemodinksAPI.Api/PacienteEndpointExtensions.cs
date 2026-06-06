using System.Security.Claims;
using HemodinksAPI.Api.Authorization;
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
            .WithSummary("Listar pacientes");

        group.MapGet("/{id}", GetPacienteById)
            .WithName("GetPacienteById")
            .WithSummary("Buscar paciente por ID");

        group.MapPost("/", CreatePaciente)
            .WithName("CreatePaciente")
            .WithSummary("Criar paciente");

        group.MapPut("/{id}", UpdatePaciente)
            .WithName("UpdatePaciente")
            .WithSummary("Atualizar paciente");

        group.MapDelete("/{id}", DeletePaciente)
            .WithName("DeletePaciente")
            .WithSummary("Excluir paciente");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadPacienteArquivo")
            .WithSummary("Enviar arquivo do paciente")
            .DisableAntiforgery();

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeletePacienteArquivo")
            .WithSummary("Excluir arquivo do paciente");
    }

    private static async Task<IResult> GetAllPacientes(
        int? page,
        int? pageSize,
        string? search,
        string? medico,
        string? convenio,
        string? procedimento,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            return Results.Ok(await mediator.Send(new GetAllPacientesQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                Medico = medico,
                Convenio = convenio,
                Procedimento = procedimento,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentUserName = currentUser.Nome
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar pacientes");
            return Results.BadRequest(new { message = "Erro ao buscar pacientes", error = ex.Message });
        }
    }

    private static async Task<IResult> GetPacienteById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var result = await mediator.Send(new GetPacienteByIdQuery(id, currentUser.Id, currentUser.PerfilId, currentUser.Nome));
            return result == null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar paciente");
            return Results.BadRequest(new { message = "Erro ao buscar paciente", error = ex.Message });
        }
    }

    private static async Task<IResult> CreatePaciente(
        CreatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            command.CurrentUserId = currentUser.Id;
            command.CurrentPerfilId = currentUser.PerfilId;
            command.CurrentUserName = currentUser.Nome;
            var result = await mediator.Send(command);
            return Results.Created($"/api/pacientes/{result.Id}", result);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao criar paciente");
            return Results.BadRequest(new { message = "Erro ao criar paciente", error = ex.Message });
        }
    }

    private static async Task<IResult> UpdatePaciente(
        int id,
        UpdatePacienteCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            command.Id = id;
            command.CurrentUserId = currentUser.Id;
            command.CurrentPerfilId = currentUser.PerfilId;
            command.CurrentUserName = currentUser.Nome;
            return Results.Ok(await mediator.Send(command));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar paciente");
            return Results.BadRequest(new { message = "Erro ao atualizar paciente", error = ex.Message });
        }
    }

    private static async Task<IResult> DeletePaciente(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            await mediator.Send(new DeletePacienteCommand { Id = id, CurrentPerfilId = currentUser.PerfilId });
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao excluir paciente");
            return Results.BadRequest(new { message = "Erro ao excluir paciente", error = ex.Message });
        }
    }

    private static async Task<IResult> UploadArquivo(
        int id,
        IFormFile file,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var result = await mediator.Send(new UploadPacienteArquivoCommand
            {
                PacienteId = id,
                File = file,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentUserName = currentUser.Nome
            });

            return Results.Created($"/api/pacientes/{id}/arquivos/{result.Id}", result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao enviar arquivo do paciente");
            return Results.BadRequest(new { message = "Erro ao enviar arquivo", error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteArquivo(
        int id,
        int arquivoId,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            await mediator.Send(new DeletePacienteArquivoCommand
            {
                PacienteId = id,
                ArquivoId = arquivoId,
                CurrentUserId = currentUser.Id,
                CurrentPerfilId = currentUser.PerfilId,
                CurrentUserName = currentUser.Nome
            });

            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao excluir arquivo do paciente");
            return Results.BadRequest(new { message = "Erro ao excluir arquivo", error = ex.Message });
        }
    }
}
