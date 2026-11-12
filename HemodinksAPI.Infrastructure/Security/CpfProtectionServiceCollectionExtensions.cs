using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Infrastructure.Security;

public static class CpfProtectionServiceCollectionExtensions
{
    internal const string ApplicationName = "HemodinksAPI.Cpf";

    /// <summary>
    /// Registra <see cref="ICpfProtector"/>. Fora de Development/Testing, certificado e chave de hash
    /// sao obrigatorios: a aplicacao nao sobe sem eles (fail-closed).
    /// </summary>
    public static IServiceCollection AddCpfProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(CpfProtectionOptions.SectionName).Get<CpfProtectionOptions>()
            ?? new CpfProtectionOptions();
        var allowEphemeral = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (options.HasCertificate != options.HasLookupHashKey)
        {
            throw new InvalidOperationException(
                "CpfProtection:CertificateBase64 e CpfProtection:LookupHashKey devem ser configurados juntos.");
        }

        if (!options.HasCertificate)
        {
            if (!allowEphemeral)
            {
                throw new InvalidOperationException(
                    "CpfProtection:CertificateBase64 e CpfProtection:LookupHashKey devem ser configurados.");
            }

            services.AddSingleton<ICpfProtector>(provider => CreateEphemeral(provider.GetRequiredService<ILoggerFactory>()));
        }
        else
        {
            // Valida no registro para falhar no startup, nao na primeira gravacao.
            var certificate = CpfProtectionKeyMaterial.LoadCertificate(options);
            var lookupHashKey = CpfProtectionKeyMaterial.LoadLookupHashKey(options);
            var previousCertificates = CpfProtectionKeyMaterial.LoadPreviousCertificates(options);
            var previousLookupHashKeys = CpfProtectionKeyMaterial.LoadPreviousLookupHashKeys(options);
            services.AddSingleton<ICpfProtector>(provider => CreatePersistent(
                certificate,
                lookupHashKey,
                new SqlDataProtectionXmlRepository(provider.GetRequiredService<ISqlConnectionFactory>()),
                provider.GetRequiredService<ILoggerFactory>(),
                previousCertificates,
                previousLookupHashKeys));
        }

        if (!options.HasCertificate && (options.PreviousCertificates.Count > 0 || options.PreviousLookupHashKeys.Count > 0))
        {
            throw new InvalidOperationException(
                "CpfProtection:PreviousCertificates e PreviousLookupHashKeys exigem certificado e chave de hash atuais.");
        }

        services.AddSingleton(new CpfProtectionRuntimeInfo(
            UsesEphemeralKeys: !options.HasCertificate,
            ReconcileOnStartup: options.ReconcileOnStartup ?? !environment.IsEnvironment("Testing")));
        services.AddHostedService<CpfProtectionReconciliationService>();

        return services;
    }

    internal static DataProtectionCpfProtector CreatePersistent(
        X509Certificate2 certificate,
        byte[] lookupHashKey,
        IXmlRepository repository,
        ILoggerFactory loggerFactory,
        IReadOnlyList<X509Certificate2>? previousCertificates = null,
        IReadOnlyList<byte[]>? previousLookupHashKeys = null)
    {
        X509Certificate2[] readableCertificates = [certificate, .. previousCertificates ?? []];
        ServiceProvider Build()
        {
            // Provider isolado: chaves dedicadas ao CPF, sem alterar o Data Protection padrao da aplicacao.
            var services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            services.AddDataProtection()
                .SetApplicationName(ApplicationName)
                .ProtectKeysWithCertificate(certificate)
                // Anteriores: so para ler chaves antigas durante a rotacao. Chaves novas usam o atual.
                .UnprotectKeysWithAnyCertificate(readableCertificates)
                .AddKeyManagementOptions(keyOptions => keyOptions.XmlRepository = repository);
            return services.BuildServiceProvider();
        }

        var serviceProvider = Build();

        return new DataProtectionCpfProtector(
            serviceProvider.GetRequiredService<IDataProtectionProvider>(),
            lookupHashKey,
            serviceProvider,
            previousLookupHashKeys,
            new CpfCertificateRotation(
                repository,
                serviceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>(),
                certificate),
            () =>
            {
                var rebuilt = Build();
                return (rebuilt.GetRequiredService<IDataProtectionProvider>(), rebuilt);
            });
    }

    internal static DataProtectionCpfProtector CreateEphemeral(ILoggerFactory loggerFactory)
    {
        // Apenas Development/Testing: chaves em memoria, perdidas ao reiniciar; o reconciliador regrava.
        var key = RandomNumberGenerator.GetBytes(CpfProtectionKeyMaterial.MinimumLookupHashKeyBytes);
        return new DataProtectionCpfProtector(
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(loggerFactory),
            key);
    }
}

public sealed record CpfProtectionRuntimeInfo(bool UsesEphemeralKeys, bool ReconcileOnStartup);
