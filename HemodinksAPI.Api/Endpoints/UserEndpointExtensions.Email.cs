using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Features.Users.Commands;

namespace HemodinksAPI.Api;

public static partial class UserEndpointExtensions
{
    public sealed record RequestEmailChangeBody(string SenhaAtual, string NovoEmail);
    public sealed record ConfirmEmailChangeBody(Guid RequestId, string Code);
    public sealed record CancelEmailChangeBody(Guid RequestId);

    private static Task<IResult> RequestEmailChange(RequestEmailChangeBody body, HttpContext http,
        SensitiveIdentityService identity, ILogger<Program> logger, CancellationToken ct) =>
        EndpointExecution.RunAsync(async () =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (body.SenhaAtual == null || body.NovoEmail == null) return Results.BadRequest(new { code = "invalid_payload" });
            return Results.Ok(await identity.RequestEmailAsync(GetRequiredCurrentUser(http.User), body.SenhaAtual, body.NovoEmail, ct));
        }, logger, "Erro ao solicitar alteração de email", "Erro ao solicitar alteração de email");

    private static Task<IResult> ConfirmEmailChange(ConfirmEmailChangeBody body, HttpContext http,
        SensitiveIdentityService identity, AuthenticationSessionService sessions, AuthenticationSessionCookie cookie,
        IConfiguration configuration, ILogger<Program> logger, CancellationToken ct) =>
        EndpointExecution.RunAsync(async () =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (body.Code == null) return Results.BadRequest(new { code = "invalid_payload" });
            var actor = GetRequiredCurrentUser(http.User);
            var securityVersion = await identity.ConfirmEmailAsync(actor, body.RequestId, body.Code, ct);
            // Todas as sessões já foram revogadas. Só quem acabou de provar senha e posse do novo email recebe
            // uma sessão nova, e só em requisição confiável (mesma regra do login/renovação) (#335).
            // Decisão consciente: o limite absoluto (12 h) recomeça, pois senha + código equivalem a um login completo.
            var renewed = SessionRequestSecurity.IsTrusted(http, configuration)
                && await TryRenewSessionAsync(actor, securityVersion, http, sessions, cookie, logger, ct);
            return Results.Ok(renewed
                ? new { code = "identity_changed", sessionRenewed = true, message = "Email alterado. Você continua conectado." }
                : new { code = "identity_changed", sessionRenewed = false, message = "Email alterado. Entre novamente com o novo email e a mesma senha." });
        }, logger, "Erro ao confirmar alteração de email", "Erro ao confirmar alteração de email");

    // O email já foi trocado: uma falha aqui não pode virar erro da confirmação, só cai no fluxo de novo login.
    private static async Task<bool> TryRenewSessionAsync(CurrentUserContext actor, Guid securityVersion, HttpContext http,
        AuthenticationSessionService sessions, AuthenticationSessionCookie cookie, ILogger<Program> logger, CancellationToken ct)
    {
        try
        {
            var issued = await sessions.StartAsync(actor.UsuarioGlobalId, actor.Id, actor.ClinicaId,
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), ct, securityVersion);
            if (issued == null) return false;
            http.Items[SecurityObservationMiddleware.IssuedClinic] = issued.Identity.ClinicaId;
            cookie.Write(http, issued);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Sessão não renovada após a alteração de email; o titular entrará novamente.");
            return false;
        }
    }

    private static Task<IResult> CancelEmailChange(CancelEmailChangeBody body, HttpContext http,
        SensitiveIdentityService identity, ILogger<Program> logger, CancellationToken ct) =>
        EndpointExecution.RunAsync(async () =>
        {
            http.Response.Headers.CacheControl = "no-store";
            await identity.CancelEmailAsync(GetRequiredCurrentUser(http.User), body.RequestId, ct);
            return Results.NoContent();
        }, logger, "Erro ao cancelar alteração de email", "Erro ao cancelar alteração de email");
}
