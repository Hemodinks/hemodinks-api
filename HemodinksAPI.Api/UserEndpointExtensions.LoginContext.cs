using HemodinksAPI.Application.Features.Users.Commands;
using MediatR;

namespace HemodinksAPI.Api;

public static partial class UserEndpointExtensions
{
    private static Task<IResult> ResolveLoginClinics(
        ResolveLoginClinicsCommand command,
        IMediator mediator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            var result = await mediator.Send(command, cancellationToken);
            return Results.Ok(result);
        }, logger, "Falha ao resolver contexto de login", "Erro ao autenticar usuario", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }
}
