namespace HemodinksAPI.Infrastructure.Utils;

/// <summary>Preserves the independent operator PIN policy; password upgrades do not migrate PINs.</summary>
public sealed class PinHasher : IPinHasher
{
    public string HashPin(string pin) => PasswordHasher.HashWithIterations(pin, 210_000);

    public bool VerifyPin(string pin, string hash) =>
        PasswordHasher.VerifyCore(pin, hash) != PasswordVerificationResult.Failed;
}
