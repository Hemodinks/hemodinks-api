using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Features.Sessions;

namespace HemodinksAPI.Api;

// Only validated typed input is pseudonymized; bodies, headers and endpoint results are never logged.
public sealed class SecurityAccountObservationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var email = context.Arguments.Select(x => x switch
        {
            AuthenticateUserCommand command => command.Email,
            ResolveLoginClinicsCommand command => command.Email,
            ResetUserPasswordByEmailCommand command => command.Email,
            _ => null
        }).FirstOrDefault(x => x != null);
        try
        {
            if (email != null)
                context.HttpContext.Items[SecurityObservationMiddleware.Account] = context.HttpContext.RequestServices
                    .GetRequiredService<ISecurityObservationWriter>().PseudonymizeAccount(email);
        }
        catch (Exception) { } // Telemetry is never an authorization prerequisite.
        return await next(context);
    }
}

public sealed class SecurityObservationMiddleware(RequestDelegate next)
{
    internal static readonly object Account = new();
    internal static readonly object Failure = new();
    internal static readonly object IssuedClinic = new();
    internal static readonly object CredentialOnly = new();
    internal static readonly object Revoked = new();

    public async Task InvokeAsync(HttpContext context, ISecurityObservationWriter writer, TimeProvider clock)
    {
        var operation = Operation(context);
        var threw = false;
        try { await next(context); }
        catch { threw = true; throw; }
        finally
        {
            try
            {
                var status = threw ? 500 : context.Response.StatusCode;
                var code = context.Items[Failure] as string;
                var snapshot = ValidatedSessionRequest.Get(context);
                if (code != null) operation = "session";
                var kind = Classify(operation, status, code,
                    context.Items.ContainsKey(IssuedClinic), context.Items.ContainsKey(CredentialOnly),
                    context.Items.ContainsKey(Revoked));
                if (kind == null && status == 401 && context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() != null)
                {
                    kind = snapshot == null ? SecurityEventKind.SessionRejected : SecurityEventKind.AuthorizationDenied;
                    operation = "authorization"; // Missing/invalid bearer is not proof of session expiration.
                }
                if (kind == null && (snapshot != null
                    || context.Items.ContainsKey(IssuedClinic) && operation is "session" or "bootstrap"))
                {
                    kind = SecurityEventKind.SessionValidated;
                    operation = "session";
                }
                if (kind.HasValue)
                {
                    // Pre-login failures remain platform-only, even with a valid bearer or clinic header.
                    var clinic = context.Items[IssuedClinic] as int?;
                    if (operation is not ("login" or "discovery" or "operator" or "recovery"))
                        clinic ??= snapshot?.ClinicaId;
                    writer.TryWrite(new(clock.GetUtcNow(), kind.Value, operation,
                        Reason(kind.Value, code, status), SafeRequestId(context.TraceIdentifier), clinic,
                        context.Items[Account] as string));
                }
            }
            catch (Exception) { } // Includes a throwing/replaced sink: retain the original auth result.
        }
    }

    internal static SecurityEventKind? Classify(string operation, int status, string? failure,
        bool issued = false, bool credentialOnly = false, bool revoked = false)
    {
        if (status >= 500) return failure == AuthenticationSessionValidation.TemporarilyUnavailable
            ? SecurityEventKind.TemporaryConflict : operation != "other" || failure != null
                ? SecurityEventKind.InfrastructureFailure : null;
        if (status == 429 && operation != "other") return SecurityEventKind.Throttled;
        if (failure is SessionLifetimePolicy.AbsoluteExpired or SessionLifetimePolicy.IdleExpired)
            return SecurityEventKind.SessionExpired;
        if (failure != null) return SecurityEventKind.SessionRejected;
        if (status == 403) return SecurityEventKind.AuthorizationDenied;
        if (operation is "login" or "discovery" or "operator")
        {
            if (status is 400 or 401) return SecurityEventKind.AuthenticationRefused;
            if (status is >= 200 and < 300)
                return issued ? SecurityEventKind.AuthenticationSucceeded
                    : operation == "discovery" || credentialOnly ? SecurityEventKind.CredentialValidated : null;
        }
        if (operation is "recovery" or "recovery_confirm" && status is >= 400 and < 500) return SecurityEventKind.RecoveryRefused;
        if (operation == "recovery" && status is >= 200 and < 300) return SecurityEventKind.RecoveryRequested;
        if (operation == "recovery_confirm" && status is >= 200 and < 300) return SecurityEventKind.RecoveryCompleted;
        if (operation == "credential_change" && status is >= 200 and < 300) return SecurityEventKind.CredentialChanged;
        if (operation == "credential_revoke" && status is >= 200 and < 300) return SecurityEventKind.CredentialRevoked;
        if (revoked) return SecurityEventKind.SessionRevoked;
        if (operation is "session" or "bootstrap")
        {
            if (status == 409) return SecurityEventKind.TemporaryConflict;
            if (status == 401) return SecurityEventKind.SessionRejected;
        }
        return null;
    }

    private static string Reason(SecurityEventKind kind, string? code, int status) => kind switch
    {
        SecurityEventKind.AuthenticationRefused => status == 400 ? "invalid_request" : "invalid_credential",
        SecurityEventKind.RecoveryRefused => "invalid_request",
        SecurityEventKind.AuthorizationDenied => "authorization_denied",
        SecurityEventKind.InfrastructureFailure => "infrastructure_failure",
        SecurityEventKind.TemporaryConflict => code == AuthenticationSessionValidation.TemporarilyUnavailable
            ? "session_validation_busy" : "session_refresh_conflict",
        SecurityEventKind.SessionExpired => code == SessionLifetimePolicy.AbsoluteExpired ? "absolute_expired" : "idle_expired",
        SecurityEventKind.SessionRejected => "session_invalid",
        SecurityEventKind.Throttled => "rate_limited",
        _ => "completed"
    };

    private static string Operation(HttpContext context) => context.GetEndpoint()?.Metadata
        .GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName switch
    {
        "AuthenticateUser" => "login", "ResolveLoginClinics" => "discovery",
        "IdentifyTeamOperator" => "operator", "ResetPasswordByEmail" => "recovery",
        "ConfirmPasswordReset" => "recovery_confirm",
        "ChangePassword" or "ConfirmEmailChange" => "credential_change",
        "ChangeTemporaryPassword" or "ResetPassword" => "credential_revoke",
        "RestoreSession" => "bootstrap",
        "RefreshSession" or "EndSession" or "TouchSessionActivity" => "session",
        _ => "other"
    };

    internal static string SafeRequestId(string id) => new(id.Take(100)
        .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':').ToArray());
}
