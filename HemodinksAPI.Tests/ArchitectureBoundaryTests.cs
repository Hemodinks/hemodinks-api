using System.Reflection;
using HemodinksAPI.Application;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace HemodinksAPI.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Domain_does_not_reference_outer_layers()
    {
        var references = ReferencedAssemblies(typeof(Paciente).Assembly);

        Assert.DoesNotContain("HemodinksAPI.Application", references);
        Assert.DoesNotContain("HemodinksAPI.Infrastructure", references);
        Assert.DoesNotContain("HemodinksAPI.Api", references);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var references = ReferencedAssemblies(typeof(ApplicationServiceCollectionExtensions).Assembly);

        Assert.DoesNotContain("HemodinksAPI.Infrastructure", references);
        Assert.DoesNotContain("HemodinksAPI.Api", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.SqlServer", references);
        Assert.DoesNotContain(references, reference =>
            reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_does_not_expose_a_general_purpose_db_context()
    {
        var applicationAssembly = typeof(ApplicationServiceCollectionExtensions).Assembly;

        Assert.Null(applicationAssembly.GetType("HemodinksAPI.Application.Data.IAppDbContext"));
    }

    [Fact]
    public void Feature_data_contracts_do_not_expose_unrelated_aggregates()
    {
        var teamProperties = typeof(ITeamDbContext).GetProperties().Select(property => property.Name).ToHashSet();
        var financeProperties = typeof(IFinanceEndpointDbContext).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.DoesNotContain(nameof(IPatientDataDbContext.Pacientes), teamProperties);
        Assert.DoesNotContain(nameof(IFinancialDataDbContext.Faturamentos), teamProperties);
        Assert.DoesNotContain(nameof(IUserDbContext.Users), financeProperties);
        Assert.DoesNotContain(nameof(IPatientDataDbContext.Pacientes), financeProperties);
    }

    [Fact]
    public void Critical_user_queries_and_password_flows_use_narrow_data_contracts()
    {
        var searchProperties = DataContractProperties(typeof(IUserSearchDbContext));
        var profileProperties = DataContractProperties(typeof(IProfileDirectoryDbContext));

        Assert.Equal(
            [nameof(IUserSearchDbContext.EquipeMembros), nameof(IUserDbContext.Users)],
            searchProperties.Order().ToArray());
        Assert.Equal([nameof(IProfileDirectoryDbContext.Perfis)], profileProperties.ToArray());
        Assert.DoesNotContain(nameof(IPatientDataDbContext.Pacientes), searchProperties);
        Assert.DoesNotContain(nameof(IFinancialDataDbContext.Faturamentos), searchProperties);
        Assert.False(typeof(IUnitOfWork).IsAssignableFrom(typeof(IDashboardFeatureDbContext)));
    }

    [Fact]
    public void Authentication_session_use_case_depends_on_a_persistence_port()
    {
        var dependencies = typeof(AuthenticationSessionService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IAuthenticationSessionStore), dependencies);
        Assert.DoesNotContain(dependencies, dependency =>
            dependency.Name.EndsWith("DbContext", StringComparison.Ordinal));
    }

    [Fact]
    public void Http_endpoint_handlers_do_not_depend_on_concrete_db_context()
    {
        var concreteContextHandlers = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("EndpointExtensions", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(method => new { Type = type, Method = method }))
            .Where(item => item.Method.ReturnType == typeof(Task<IResult>))
            .Where(item => item.Method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AppDbContext)))
            .Select(item => $"{item.Type.Name}.{item.Method.Name}")
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(concreteContextHandlers);
    }

    [Fact]
    public void Only_platform_administration_endpoints_access_data_contexts_directly()
    {
        var endpointHandlersWithDataContext = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("EndpointExtensions", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(method => new { Type = type, Method = method }))
            .Where(item => item.Method.ReturnType == typeof(Task<IResult>))
            .Where(item => item.Method.GetParameters().Any(parameter =>
                parameter.ParameterType.Name.EndsWith("DbContext", StringComparison.Ordinal)))
            .Select(item => item.Type.Name)
            .Distinct()
            .ToArray();

        Assert.All(endpointHandlersWithDataContext, typeName =>
            Assert.Equal("ClinicaPlatformEndpointExtensions", typeName));
    }

    [Fact]
    public void Finance_feature_does_not_depend_on_patient_command_internals()
    {
        var forbiddenNamespace = "HemodinksAPI.Application.Features.Pacientes.Commands";
        var invalidDependencies = typeof(ApplicationServiceCollectionExtensions).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "HemodinksAPI.Application.Features.Financeiro")
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => new { Owner = type.FullName, Dependency = parameter.ParameterType }))
            .Where(item => item.Dependency.Namespace == forbiddenNamespace)
            .Select(item => $"{item.Owner} -> {item.Dependency.FullName}")
            .ToArray();

        Assert.Empty(invalidDependencies);
    }

    private static HashSet<string> ReferencedAssemblies(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> DataContractProperties(Type contract)
    {
        return contract.GetInterfaces()
            .Append(contract)
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
