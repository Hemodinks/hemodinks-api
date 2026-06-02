using System.Security.Claims;
using HemodinksAPI.Api.Features.Users.Commands;
using HemodinksAPI.Api.Features.Users.Queries;
using MediatR;

namespace HemodinksAPI.Api;

/// <summary>
/// Extensoes para mapear endpoints de usuarios.
/// </summary>
public static class UserEndpointExtensions
{
    /// <summary>
    /// Mapear endpoints de usuarios.
    /// </summary>
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Criar novo usuario")
            .WithDescription("Cria um novo usuario com a senha padrao")
            .RequireAuthorization();

        group.MapPost("/authenticate", AuthenticateUser)
            .WithName("AuthenticateUser")
            .WithSummary("Autenticar usuario")
            .WithDescription("Autentica um usuario e retorna um token JWT");

        group.MapGet("/", GetAllUsers)
            .WithName("GetAllUsers")
            .WithSummary("Listar todos os usuarios")
            .WithDescription("Retorna uma lista de todos os usuarios cadastrados")
            .RequireAuthorization();

        group.MapGet("/{id}", GetUserById)
            .WithName("GetUserById")
            .WithSummary("Buscar usuario por ID")
            .WithDescription("Retorna os dados de um usuario especifico")
            .RequireAuthorization();

        group.MapGet("/email/{email}", GetUserByEmail)
            .WithName("GetUserByEmail")
            .WithSummary("Buscar usuario por email")
            .WithDescription("Retorna os dados de um usuario pelo email")
            .RequireAuthorization();

        group.MapPut("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithSummary("Atualizar usuario")
            .WithDescription("Atualiza os dados cadastrais de um usuario")
            .RequireAuthorization();

        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Excluir usuario")
            .WithDescription("Remove um usuario cadastrado")
            .RequireAuthorization();

        group.MapPut("/{id}/password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Alterar senha")
            .WithDescription("Altera a senha do usuario autenticado")
            .RequireAuthorization();

        group.MapPost("/password/reset", ResetPasswordByEmail)
            .WithName("ResetPasswordByEmail")
            .WithSummary("Resetar senha por email")
            .WithDescription("Reseta a senha do usuario para a senha padrao pelo email informado");

        group.MapPut("/{id}/password/reset", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Resetar senha")
            .WithDescription("Reseta a senha do usuario para a senha padrao e obriga troca no proximo login")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateUser(
        CreateUserCommand command,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(command);
            return Results.Created($"/api/users/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao criar usuario");
            return Results.BadRequest(new { message = "Erro ao criar usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> AuthenticateUser(
        AuthenticateUserCommand command,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Falha na autenticacao");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao autenticar usuario");
            return Results.BadRequest(new { message = "Erro ao autenticar usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> GetAllUsers(
        int? page,
        int? pageSize,
        string? search,
        int? profileId,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(new GetAllUsersQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                ProfileId = profileId
            });
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar usuarios");
            return Results.BadRequest(new { message = "Erro ao buscar usuarios", error = ex.Message });
        }
    }

    private static async Task<IResult> GetUserById(
        int id,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(new GetUserByIdQuery(id));
            return result == null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar usuario por ID");
            return Results.BadRequest(new { message = "Erro ao buscar usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> GetUserByEmail(
        string email,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(new GetUserByEmailQuery(email));
            return result == null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar usuario por email");
            return Results.BadRequest(new { message = "Erro ao buscar usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateUser(
        int id,
        UpdateUserCommand command,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            command.Id = id;
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar usuario");
            return Results.BadRequest(new { message = "Erro ao atualizar usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteUser(
        int id,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            await mediator.Send(new DeleteUserCommand { Id = id });
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao excluir usuario");
            return Results.BadRequest(new { message = "Erro ao excluir usuario", error = ex.Message });
        }
    }

    private static async Task<IResult> ChangePassword(
        int id,
        ChangePasswordCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var loggedUserId = claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(loggedUserId, out var authenticatedUserId) || authenticatedUserId != id)
            {
                return Results.Forbid();
            }

            command.UserId = id;
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao alterar senha");
            return Results.BadRequest(new { message = "Erro ao alterar senha", error = ex.Message });
        }
    }

    private static async Task<IResult> ResetPassword(
        int id,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(new ResetUserPasswordCommand { UserId = id });
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao resetar senha");
            return Results.BadRequest(new { message = "Erro ao resetar senha", error = ex.Message });
        }
    }

    private static async Task<IResult> ResetPasswordByEmail(
        ResetUserPasswordByEmailCommand command,
        IMediator mediator,
        ILogger<Program> logger)
    {
        try
        {
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { message = "Email nao encontrado." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao resetar senha por email");
            return Results.BadRequest(new { message = "Erro ao resetar senha", error = ex.Message });
        }
    }
}
