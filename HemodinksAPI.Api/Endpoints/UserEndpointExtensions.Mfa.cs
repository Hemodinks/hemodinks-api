using System.Security.Claims;
using HemodinksAPI.Application.Features.Users.Mfa;
using MediatR;

namespace HemodinksAPI.Api;

// #150: verificacao em duas etapas (TOTP) para Administrador e SuperAdministrador.
public static partial class UserEndpointExtensions
{
    public sealed record MfaCodeRequest(string? Codigo);
    public sealed record MfaDisableRequest(string? Senha, string? Codigo);

    internal static void MapUserMfaEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/authenticate/mfa", CompleteMfaLogin)
            .WithName("CompleteMfaLogin")
            .WithSummary("Concluir login com o segundo fator")
            .AllowAnonymous()
            .AddEndpointFilter<AuthenticationRateLimitFilter>();

        group.MapGet("/me/mfa", async (ClaimsPrincipal principal, MfaService mfa, CancellationToken ct) =>
                Results.Ok(await mfa.GetStatusAsync(GetRequiredCurrentUser(principal), ct)))
            .WithName("GetMyMfaStatus").RequireAuthorization();

        group.MapPost("/me/mfa/configuracao", (ClaimsPrincipal principal, MfaService mfa, HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
                EndpointExecution.RunAsync(async () =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(await mfa.BeginSetupAsync(GetRequiredCurrentUser(principal), ct));
                }, logger, "Falha ao configurar MFA", "Erro ao configurar a verificacao em duas etapas"))
            .WithName("BeginMyMfaSetup").RequireAuthorization("Administrador").RequireRateLimiting("PasswordReset");

        group.MapPost("/me/mfa/ativacao", (MfaCodeRequest request, ClaimsPrincipal principal, MfaService mfa, HttpContext http,
                ILogger<Program> logger, CancellationToken ct) =>
                EndpointExecution.RunAsync(async () =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(await mfa.ActivateAsync(GetRequiredCurrentUser(principal), request.Codigo, ct));
                }, logger, "Falha ao ativar MFA", "Erro ao ativar a verificacao em duas etapas"))
            .WithName("ActivateMyMfa").RequireAuthorization("Administrador").RequireRateLimiting("PasswordReset");

        group.MapPost("/me/mfa/desativacao", (MfaDisableRequest request, ClaimsPrincipal principal, MfaService mfa,
                ILogger<Program> logger, CancellationToken ct) =>
                EndpointExecution.RunAsync(async () =>
                {
                    await mfa.DeactivateAsync(GetRequiredCurrentUser(principal), request.Senha, request.Codigo, ct);
                    return Results.NoContent();
                }, logger, "Falha ao desativar MFA", "Erro ao desativar a verificacao em duas etapas"))
            .WithName("DeactivateMyMfa").RequireAuthorization().RequireRateLimiting("PasswordReset");

        group.MapPost("/{id:int}/mfa/redefinicao", (int id, ClaimsPrincipal principal, MfaService mfa,
                ILogger<Program> logger, CancellationToken ct) =>
                EndpointExecution.RunAsync(async () =>
                {
                    await mfa.ResetAsync(GetRequiredCurrentUser(principal), id, ct);
                    return Results.NoContent();
                }, logger, "Falha ao redefinir MFA", "Erro ao redefinir a verificacao em duas etapas"))
            .WithName("ResetUserMfa").RequireAuthorization("SuperAdministrador");
    }

    private static Task<IResult> CompleteMfaLogin(
        CompleteMfaLoginCommand command,
        HttpContext httpContext,
        IMediator mediator,
        HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService sessionService,
        AuthenticationSessionCookie sessionCookie,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        return EndpointExecution.RunAsync(async () =>
        {
            if (!SessionRequestSecurity.IsTrusted(httpContext, httpContext.RequestServices.GetRequiredService<IConfiguration>(), login: true))
                return Results.StatusCode(403);
            var result = await mediator.Send(command, cancellationToken);
            httpContext.Response.Headers.CacheControl = "no-store";
            var session = await SessionLoginIssuer.StartAsync(result, httpContext, sessionService, cancellationToken);
            if (session == null)
            {
                return Results.Unauthorized();
            }

            httpContext.Items[SecurityObservationMiddleware.IssuedClinic] = result.ClinicaId;
            result.Token = session.AccessToken;
            sessionCookie.Write(httpContext, session);
            return Results.Ok(result);
        }, logger, "Falha na verificacao em duas etapas", "Erro ao concluir o login", new EndpointErrorOptions
        {
            UnauthorizedAccessAsUnauthorized = true
        });
    }
}
