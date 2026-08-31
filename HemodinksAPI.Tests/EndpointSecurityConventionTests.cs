using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class EndpointSecurityConventionTests
{
    [Fact]
    public void Authorization_UsesAuthenticatedFallbackPolicy()
    {
        using var factory = new HemodinksApiFactory();
        _ = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task DetailedHealthEndpoint_WhenAnonymous_ReturnsUnauthorized()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/monitoramento/saude");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void PatientFileUploadEndpoints_DefineRequestBodyLimitsBeforeFormBinding()
    {
        using var factory = new HemodinksApiFactory();
        _ = factory.CreateClient();
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            .Where(endpoint => endpoint.RoutePattern.RawText is
                "/api/users/{id}/arquivos"
                or "/api/atendimentos-cirurgicos/{id:int}/arquivos"
                or "/api/financeiro/contas-receber/recebimentos/{id:int}/comprovante"
                or "/api/faturamentos-medicos/historico/{ano:int}/{mes:int}/arquivos")
            .ToList();

        Assert.Equal(4, routes.Count);
        Assert.All(routes, endpoint =>
        {
            var limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
            Assert.NotNull(limit);
            Assert.InRange(limit.MaxRequestBodySize.GetValueOrDefault(), 10 * 1024 * 1024, 11 * 1024 * 1024);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });
    }
}
