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
        SensitiveIdentityService identity, ILogger<Program> logger, CancellationToken ct) =>
        EndpointExecution.RunAsync(async () =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (body.Code == null) return Results.BadRequest(new { code = "invalid_payload" });
            await identity.ConfirmEmailAsync(GetRequiredCurrentUser(http.User), body.RequestId, body.Code, ct);
            return Results.Ok(new { code = "identity_changed", message = "Email alterado. Entre novamente." });
        }, logger, "Erro ao confirmar alteração de email", "Erro ao confirmar alteração de email");

    private static Task<IResult> CancelEmailChange(CancelEmailChangeBody body, HttpContext http,
        SensitiveIdentityService identity, ILogger<Program> logger, CancellationToken ct) =>
        EndpointExecution.RunAsync(async () =>
        {
            http.Response.Headers.CacheControl = "no-store";
            await identity.CancelEmailAsync(GetRequiredCurrentUser(http.User), body.RequestId, ct);
            return Results.NoContent();
        }, logger, "Erro ao cancelar alteração de email", "Erro ao cancelar alteração de email");
}
