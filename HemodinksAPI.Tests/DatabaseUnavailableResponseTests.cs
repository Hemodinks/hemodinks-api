using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Features.Users.Commands;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class DatabaseUnavailableResponseTests
{
    // Nothing listens here: fails fast without any external call.
    private const string Secret = "Not-A-Real-Password-503";
    private const string UnreachableConnection =
        $"Server=tcp:127.0.0.1,1;Database=HemodinksSecretDb;User Id=probe;Password={Secret};Connect Timeout=2;Encrypt=False";

    [Theory]
    [InlineData(40613, 17)] // Azure SQL database not available (serverless resume).
    [InlineData(40197, 17)]
    [InlineData(40501, 20)]
    [InlineData(-2, 11)] // Timeout.
    [InlineData(10060, 20)]
    [InlineData(0, 20)] // Connection-level failure with a platform-specific number.
    public void AvailabilityErrors_AreRecognized(int number, byte errorClass) =>
        Assert.True(DatabaseAvailability.IsUnavailableSqlError(number, errorClass));

    [Theory]
    [InlineData(18456, 14)] // Wrong credentials: configuration problem, not "starting".
    [InlineData(229, 14)] // Permission denied.
    [InlineData(40615, 14)] // Firewall.
    [InlineData(102, 15)] // Syntax error: bug.
    [InlineData(547, 16)] // Constraint violation.
    [InlineData(1205, 13)] // Deadlock.
    public void OtherSqlErrors_AreNotReportedAsUnavailable(int number, byte errorClass) =>
        Assert.False(DatabaseAvailability.IsUnavailableSqlError(number, errorClass));

    [Fact]
    public async Task RealConnectionFailure_Returns503WithoutLeakingDetails()
    {
        var failure = await CaptureConnectionFailureAsync();

        var (status, retryAfter, cacheControl, body) = await ExecuteAsync(ApiExceptionResults.Map(failure));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("5", retryAfter);
        Assert.Equal("no-store", cacheControl);
        Assert.Equal("database_unavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("HemodinksSecretDb", body);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("SqlException", body);
    }

    [Fact]
    public async Task WrappedFailures_AreRecognized()
    {
        var failure = await CaptureConnectionFailureAsync();

        Assert.True(DatabaseAvailability.IsUnavailable(new DbUpdateException("save", failure)));
        Assert.True(DatabaseAvailability.IsUnavailable(new InvalidOperationException("ef", failure)));
        Assert.True(DatabaseAvailability.IsUnavailable(new RetryLimitExceededException("retries", failure)));
    }

    [Fact]
    public async Task RetriesExhausted_ForAnotherCause_AreNotReportedAsUnavailable() =>
        Assert.False(DatabaseAvailability.IsUnavailable(
            new RetryLimitExceededException("retries", new ArgumentException("other cause"))));

    [Fact]
    public async Task AuthorizationOutcome_CausedByDatabaseFailure_KeepsItsMeaning()
    {
        var failure = await CaptureConnectionFailureAsync();

        Assert.False(DatabaseAvailability.IsUnavailable(new UnauthorizedAccessException("denied", failure)));
        Assert.False(DatabaseAvailability.IsUnavailable(new KeyNotFoundException("not found", failure)));
    }

    [Fact]
    public async Task LoginWithDatabaseUnavailable_DoesNotRevealAccountOrClinic()
    {
        DatabaseDownBehavior<ResolveLoginClinicsCommand, ResolveLoginClinicsResponse>.Failure = await CaptureConnectionFailureAsync();
        await using var api = new HemodinksApiFactory();
        using var factory = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IPipelineBehavior<ResolveLoginClinicsCommand, ResolveLoginClinicsResponse>,
                DatabaseDownBehavior<ResolveLoginClinicsCommand, ResolveLoginClinicsResponse>>()));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var bodies = new List<string>();
        foreach (var (email, clinic) in new[]
        {
            ("exists@example.com", "clinica-a"),
            ("exists@example.com", "clinica-b"),
            ("nobody@example.com", "clinica-a")
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/login-context")
            {
                Content = JsonContent.Create(new { email, senha = "Not-A-Real-Password-503" })
            };
            request.Headers.Add("X-Clinica-Slug", clinic);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync())!;
            fields.Remove("requestId");
            bodies.Add(JsonSerializer.Serialize(fields));
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Middleware_Returns503ForDatabaseFailure()
    {
        var failure = await CaptureConnectionFailureAsync();
        var context = CreateContext();
        var middleware = new ApiExceptionHandlingMiddleware(_ => throw failure,
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public async Task EndpointExecution_Returns503ForLoginPath()
    {
        var failure = await CaptureConnectionFailureAsync();

        var result = await EndpointExecution.RunAsync(() => throw failure, NullLogger.Instance,
            "Falha", "Erro ao autenticar usuario", new EndpointErrorOptions { UnauthorizedAccessAsUnauthorized = true });

        Assert.IsType<DatabaseUnavailableResult>(result);
    }

    [Fact]
    public async Task UnrelatedFailure_Stays500AndHidesItsMessage()
    {
        var (status, retryAfter, _, body) = await ExecuteAsync(
            ApiExceptionResults.Map(new ArgumentException("internal detail")));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Null(retryAfter);
        Assert.DoesNotContain("internal detail", body);
    }

    [Fact]
    public void ExpectedOutcomes_AreNotConvertedTo503()
    {
        Assert.False(DatabaseAvailability.IsUnavailable(new UnauthorizedAccessException("Email ou senha invalidos")));
        Assert.False(DatabaseAvailability.IsUnavailable(new KeyNotFoundException("not found")));
        Assert.IsNotType<DatabaseUnavailableResult>(ApiExceptionResults.Map(
            new UnauthorizedAccessException("Email ou senha invalidos"),
            new EndpointErrorOptions { UnauthorizedAccessAsUnauthorized = true }));
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task RealSyntaxError_Stays500()
    {
        SqlServerTestConnection.RequireConfigured();
        await using var connection = new SqlConnection(SqlServerTestConnection.Create("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELEC 1";

        var failure = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

        var (status, _, _, _) = await ExecuteAsync(ApiExceptionResults.Map(failure));
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    private sealed class DatabaseDownBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public static Exception Failure { get; set; } = new InvalidOperationException("Not configured");

        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
            throw Failure;
    }

    private static async Task<SqlException> CaptureConnectionFailureAsync()
    {
        await using var connection = new SqlConnection(UnreachableConnection);
        return await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync());
    }

    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    private static async Task<(int Status, string? RetryAfter, string? CacheControl, string Body)> ExecuteAsync(IResult result)
    {
        var context = CreateContext();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var headers = context.Response.Headers;
        return (context.Response.StatusCode,
            headers.RetryAfter.Count == 0 ? null : headers.RetryAfter.ToString(),
            headers.CacheControl.Count == 0 ? null : headers.CacheControl.ToString(), body);
    }
}
