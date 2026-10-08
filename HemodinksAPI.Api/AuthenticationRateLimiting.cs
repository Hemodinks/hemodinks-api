using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Application.Features.Users.Commands;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Api;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "AuthenticationRateLimiting";
    public int LoginPermitLimit { get; set; } = 30;
    public int OperatorPermitLimit { get; set; } = 5;
    public int RecoveryPermitLimit { get; set; } = 5;
    public int SubjectWindowSeconds { get; set; } = 300;
    public int IpPermitLimit { get; set; } = 300;
    public int IpWindowSeconds { get; set; } = 60;
    public int SessionPermitLimit { get; set; } = 300;
    public int SessionWindowSeconds { get; set; } = 60;
    public bool IsValid() => LoginPermitLimit is >= 1 and <= 10000
        && OperatorPermitLimit is >= 1 and <= 10000 && RecoveryPermitLimit is >= 1 and <= 10000
        && IpPermitLimit is >= 1 and <= 100000 && SessionPermitLimit is >= 1 and <= 100000
        && SubjectWindowSeconds is >= 1 and <= 86400 && IpWindowSeconds is >= 1 and <= 86400
        && SessionWindowSeconds is >= 1 and <= 86400;
}

// Process-local HTTP budgets. Durable credential failures remain in the existing database controls.
public sealed class AuthenticationRateLimits : IDisposable
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly PartitionedRateLimiter<string> subjects;
    private readonly PartitionedRateLimiter<string> origins;

    public AuthenticationRateLimits(IOptions<AuthenticationRateLimitOptions> options)
    {
        var settings = options.Value;
        subjects = PartitionedRateLimiter.Create<string, string>(subject =>
            RateLimitPartition.GetFixedWindowLimiter(subject, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = subject.StartsWith("login:", StringComparison.Ordinal) ? settings.LoginPermitLimit
                    : subject.StartsWith("operator:", StringComparison.Ordinal) ? settings.OperatorPermitLimit : settings.RecoveryPermitLimit,
                Window = TimeSpan.FromSeconds(settings.SubjectWindowSeconds), QueueLimit = 0
            }));
        origins = PartitionedRateLimiter.Create<string, string>(origin =>
            RateLimitPartition.GetFixedWindowLimiter(origin, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.IpPermitLimit, Window = TimeSpan.FromSeconds(settings.IpWindowSeconds), QueueLimit = 0
            }));
    }

    internal string SubjectKey(string group, string? value, bool email = false)
    {
        var bounded = value is { Length: <= 512 } ? value : "invalid";
        if (email) bounded = bounded?.Trim().ToLowerInvariant();
        return group + ":" + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(bounded ?? "invalid")));
    }

    internal RateLimitLease AcquireSubject(string subject) => subjects.AttemptAcquire(subject);
    internal RateLimitLease AcquireOrigin(System.Net.IPAddress? address) => origins.AttemptAcquire(
        (address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)?.ToString() ?? "unknown");
    public void Dispose() { subjects.Dispose(); origins.Dispose(); CryptographicOperations.ZeroMemory(key); }
}

public sealed class AuthenticationRateLimitFilter(AuthenticationRateLimits limits) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var subject = context.Arguments.Select(argument => argument switch
        {
            AuthenticateUserCommand command => limits.SubjectKey("login", command.Email, email: true),
            ResolveLoginClinicsCommand command => limits.SubjectKey("login", command.Email, email: true),
            ResetUserPasswordByEmailCommand command => limits.SubjectKey("recovery", command.Email, email: true),
            ConfirmPasswordResetCommand command => limits.SubjectKey("recovery-confirm", command.Token?.Trim()),
            IdentificarEquipeRequest request => limits.SubjectKey("operator", request.Token + ":" + request.OperadorId.ToString(CultureInfo.InvariantCulture)),
            _ => null
        }).FirstOrDefault(value => value != null);
        if (subject == null) return await next(context);
        using var accountLease = limits.AcquireSubject(subject);
        if (!accountLease.IsAcquired) return RateLimitResponse.Result(context.HttpContext, accountLease);
        // A rejected subject does not spend the clinic's shared origin budget.
        using var originLease = limits.AcquireOrigin(context.HttpContext.Connection.RemoteIpAddress);
        if (!originLease.IsAcquired) return RateLimitResponse.Result(context.HttpContext, originLease);
        return await next(context);
    }
}

internal static class RateLimitResponse
{
    internal static IResult Result(HttpContext context, RateLimitLease lease)
    {
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : 1;
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new
        {
            code = "rate_limited", message = "Muitas tentativas. Aguarde antes de tentar novamente.",
            retryAfterSeconds = seconds, requestId = SecurityObservationMiddleware.SafeRequestId(context.TraceIdentifier)
        }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}
