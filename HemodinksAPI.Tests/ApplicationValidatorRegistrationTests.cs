using HemodinksAPI.Application;
using HemodinksAPI.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class ApplicationValidatorRegistrationTests
{
    // #430: validador nao registrado nao roda no ValidationBehavior e deixa o handler receber payload invalido.
    [Fact]
    public void EveryRequestValidator_IsRegisteredForItsRequest()
    {
        var services = new ServiceCollection();
        services.AddApplicationLayer();

        var validatorTypes = typeof(ApplicationServiceCollectionExtensions).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IRequestValidator<>))
                .Select(contract => (Implementation: type, Contract: contract)))
            .ToList();

        Assert.NotEmpty(validatorTypes);
        var missing = validatorTypes
            .Where(item => !services.Any(descriptor =>
                descriptor.ServiceType == item.Contract && descriptor.ImplementationType == item.Implementation))
            .Select(item => item.Implementation.Name)
            .ToList();

        Assert.Empty(missing);
    }
}
