using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Api;

public static class LocalUserSecretsConfigurationExtensions
{
    private static readonly string[] RequiredLocalSecretKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "JwtSettings:SecretKey"
    ];

    public static void AddNonProductionUserSecretsFallback(
        this ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        var fallbackValues = BuildFallbackValues(
            configuration,
            LoadUserSecrets(),
            environment);

        if (fallbackValues.Count > 0)
        {
            configuration.AddInMemoryCollection(fallbackValues);
        }
    }

    public static IReadOnlyDictionary<string, string?> BuildFallbackValues(
        IConfiguration currentConfiguration,
        IConfiguration userSecretsConfiguration,
        IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            return new Dictionary<string, string?>();
        }

        var fallbackValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in RequiredLocalSecretKeys)
        {
            if (!string.IsNullOrWhiteSpace(currentConfiguration[key]))
            {
                continue;
            }

            var secretValue = userSecretsConfiguration[key];
            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                fallbackValues[key] = secretValue;
            }
        }

        return fallbackValues;
    }

    private static IConfiguration LoadUserSecrets() =>
        new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .Build();
}
