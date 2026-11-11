using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

/// <summary>Protetor real com chaves efemeras, compartilhado pelos testes que gravam ou buscam CPF.</summary>
internal static class TestCpfProtection
{
    public static ICpfProtector Instance { get; } =
        CpfProtectionServiceCollectionExtensions.CreateEphemeral(NullLoggerFactory.Instance);
}
