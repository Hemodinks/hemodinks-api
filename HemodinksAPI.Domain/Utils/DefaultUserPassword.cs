namespace HemodinksAPI.Domain.Utils;

/// <summary>
/// Compatibilidade para cenários de teste antigos. O valor é aleatório por processo.
/// </summary>
public static class DefaultUserPassword
{
    public static string Value { get; } = TemporaryPasswordGenerator.Generate();
}
