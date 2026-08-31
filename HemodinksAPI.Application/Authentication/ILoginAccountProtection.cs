namespace HemodinksAPI.Application.Authentication;

public interface ILoginAccountProtection
{
    Task<bool> IsLockedAsync(int usuarioGlobalId, CancellationToken cancellationToken);

    Task RegisterFailureAsync(int usuarioGlobalId, CancellationToken cancellationToken);

    Task RegisterSuccessAsync(int usuarioGlobalId, CancellationToken cancellationToken);
}

public sealed class LoginAccountProtectionOptions
{
    public const string SectionName = "LoginProtection";

    public int MaximumFailedAttempts { get; set; } = 5;

    public int AttemptWindowMinutes { get; set; } = 15;

    public int LockoutMinutes { get; set; } = 15;
}

internal sealed class NoOpLoginAccountProtection : ILoginAccountProtection
{
    public Task<bool> IsLockedAsync(int usuarioGlobalId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task RegisterFailureAsync(int usuarioGlobalId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RegisterSuccessAsync(int usuarioGlobalId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
