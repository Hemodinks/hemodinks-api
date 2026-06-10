using System.Security.Claims;
using HemodinksAPI.Api.Authorization;
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
            .RequireAuthorization("Administrador");

        group.MapPost("/authenticate", AuthenticateUser)
            .WithName("AuthenticateUser")
            .WithSummary("Autenticar usuario")
            .WithDescription("Autentica um usuario e retorna um token JWT");

        group.MapGet("/", GetAllUsers)
            .WithName("GetAllUsers")
            .WithSummary("Listar todos os usuarios")
            .WithDescription("Retorna uma lista de todos os usuarios cadastrados")
            .RequireAuthorization("Administrador");

        group.MapGet("/{id}", GetUserById)
            .WithName("GetUserById")
            .WithSummary("Buscar usuario por ID")
            .WithDescription("Retorna os dados de um usuario especifico")
            .RequireAuthorization();

        group.MapGet("/{id}/foto-perfil", GetProfilePhoto)
            .WithName("GetUserProfilePhoto")
            .WithSummary("Buscar foto de perfil")
            .WithDescription("Retorna a foto de perfil pelo storage configurado no ambiente")
            .RequireAuthorization();

        group.MapGet("/email/{email}", GetUserByEmail)
            .WithName("GetUserByEmail")
            .WithSummary("Buscar usuario por email")
            .WithDescription("Retorna os dados de um usuario pelo email")
            .RequireAuthorization("Administrador");

        group.MapPut("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithSummary("Atualizar usuario")
            .WithDescription("Atualiza os dados cadastrais de um usuario")
            .RequireAuthorization();

        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Excluir usuario")
            .WithDescription("Remove um usuario cadastrado")
            .RequireAuthorization("Administrador");

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
            .RequireAuthorization("Administrador");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadUserArquivo")
            .WithSummary("Enviar arquivo do cadastro medico")
            .WithDescription("Adiciona documento ao cadastro de um usuario medico")
            .DisableAntiforgery()
            .RequireAuthorization();

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeleteUserArquivo")
            .WithSummary("Excluir arquivo do cadastro medico")
            .RequireAuthorization();
    }

    private static Task<IResult> CreateUser(
        CreateUserCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/users/{result.Id}", result);
        }, logger, "Erro ao criar usuario", "Erro ao criar usuario");
    }

    private static Task<IResult> AuthenticateUser(
        AuthenticateUserCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Falha na autenticacao", "Erro ao autenticar usuario", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> GetAllUsers(
        int? page,
        int? pageSize,
        string? search,
        int? profileId,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetAllUsersQuery
            {
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                ProfileId = profileId
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar usuarios", "Erro ao buscar usuarios");
    }

    private static Task<IResult> GetUserById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetUserByIdQuery(id)
            {
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar usuario por ID", "Erro ao buscar usuario");
    }

    private static Task<IResult> GetProfilePhoto(
        int id,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var photo = await mediator.Send(new GetUserProfilePhotoQuery
            {
                Id = id,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return photo == null
                ? Results.NotFound()
                : Results.Stream(photo.Content, photo.ContentType);
        }, logger, "Erro ao buscar foto de perfil", "Erro ao buscar foto de perfil");
    }

    private static Task<IResult> GetUserByEmail(
        string email,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetUserByEmailQuery(email), cancellationToken);
            return result == null ? Results.NotFound() : Results.Ok(result);
        }, logger, "Erro ao buscar usuario por email", "Erro ao buscar usuario");
    }

    private static Task<IResult> UpdateUser(
        int id,
        UpdateUserCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.Id = id;
            command.CurrentUser = GetRequiredCurrentUser(claimsPrincipal);

            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao atualizar usuario", "Erro ao atualizar usuario");
    }

    private static Task<IResult> DeleteUser(
        int id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            await mediator.Send(new DeleteUserCommand { Id = id }, cancellationToken);
            return Results.NoContent();
        }, logger, "Erro ao excluir usuario", "Erro ao excluir usuario");
    }

    private static Task<IResult> ChangePassword(
        int id,
        ChangePasswordCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.UserId = id;
            command.CurrentUser = GetRequiredCurrentUser(claimsPrincipal);

            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao alterar senha", "Erro ao alterar senha", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }

    private static Task<IResult> ResetPassword(
        int id,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new ResetUserPasswordCommand { UserId = id }, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao resetar senha", "Erro ao resetar senha");
    }

    private static Task<IResult> ResetPasswordByEmail(
        ResetUserPasswordByEmailCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Erro ao resetar senha por email", "Erro ao resetar senha", new EndpointErrorOptions
        {
            NotFoundMessage = "Email nao encontrado."
        });
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
            var result = await mediator.Send(new UploadUserArquivoCommand
            {
                UserId = id,
                File = file,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return Results.Created($"/api/users/{id}/arquivos/{result.Id}", result);
        }, logger, "Erro ao enviar arquivo do usuario", "Erro ao enviar arquivo");
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
            await mediator.Send(new DeleteUserArquivoCommand
            {
                UserId = id,
                ArquivoId = arquivoId,
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return Results.NoContent();
        }, logger, "Erro ao excluir arquivo do usuario", "Erro ao excluir arquivo");
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
