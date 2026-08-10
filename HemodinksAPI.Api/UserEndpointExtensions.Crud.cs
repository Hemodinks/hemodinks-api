using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Features.Users.Queries;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class UserEndpointExtensions
{
    private static Task<IResult> CreateUser(
        CreateUserCommand command,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            command.CurrentUser = GetRequiredCurrentUser(claimsPrincipal);
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
        string? sortBy,
        string? sortDirection,
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetAllUsersQuery
            {
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal),
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(10),
                Search = search,
                ProfileId = profileId,
                SortBy = sortBy,
                SortDirection = sortDirection
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar usuarios", "Erro ao buscar usuarios");
    }

    private static Task<IResult> GetAvailableProfiles(
        ClaimsPrincipal claimsPrincipal,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(new GetAvailableProfilesQuery
            {
                CurrentUser = GetRequiredCurrentUser(claimsPrincipal)
            }, cancellationToken);

            return Results.Ok(result);
        }, logger, "Erro ao buscar perfis de usuarios", "Erro ao buscar perfis de usuarios");
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
}
