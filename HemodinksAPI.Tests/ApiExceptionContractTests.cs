using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Domain.Exceptions;
using HemodinksAPI.Domain.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

// #293: só exceção de negócio explícita expõe mensagem; falha interna vira 500 sem texto e log Error.
public sealed class ApiExceptionContractTests
{
    [Fact]
    public async Task EfTrackingFailure_Returns500WithoutInternalText_AndLogsError()
    {
        var failure = CaptureEfTrackingFailure();
        Assert.IsNotType<BusinessRuleException>(failure);

        var (status, body, level) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(LogLevel.Error, level);
        Assert.DoesNotContain("ProbeEntity", body);
        Assert.DoesNotContain("tracked", body);
        Assert.DoesNotContain(failure.Message, body);
        Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task LinqFailure_Returns500WithoutInternalText()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new[] { 1, 2 }.Single());

        var (status, body, level) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(LogLevel.Error, level);
        Assert.DoesNotContain("Sequence", body);
    }

    [Fact]
    public async Task TenantGuardFailure_DoesNotRevealClinicIdsOrEntityNames()
    {
        // Mesmo formato da mensagem do AppDbContext para ClinicaId divergente.
        var failure = new InvalidOperationException("ClinicaId divergente em Paciente. Esperado 1, recebido 2.");

        var (status, body, _) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("ClinicaId", body);
        Assert.DoesNotContain("Paciente", body);
    }

    [Fact]
    public async Task BusinessRule_Returns400WithExpectedMessage_AndLogsWarning()
    {
        var failure = Assert.Throws<BusinessRuleException>(() => SurgicalTeamRules.Validate(1, 2, 2));

        var (status, body, level) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal("Os medicos auxiliares devem ser diferentes.", Message(body));
    }

    [Fact]
    public async Task ResourceNotFound_Returns404WithExpectedMessage()
    {
        var (status, body, level) = await InvokeMiddlewareAsync(new ResourceNotFoundException("Paciente nao encontrado"));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal("Paciente nao encontrado", Message(body));
    }

    [Fact]
    public async Task DictionaryKeyNotFound_Returns404WithFixedMessage_AndLogsError()
    {
        var failure = Assert.Throws<KeyNotFoundException>(() => new Dictionary<string, int>()["chave-interna-42"]);

        var (status, body, level) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(LogLevel.Error, level);
        Assert.Equal(ResourceNotFoundException.DefaultMessage, Message(body));
        Assert.DoesNotContain("chave-interna-42", body);
    }

    [Fact]
    public async Task Concurrency_Returns409WithFixedMessage()
    {
        var failure = new DbUpdateConcurrencyException(
            "The database operation was expected to affect 1 row(s), but actually affected 0 row(s).");

        var (status, body, _) = await InvokeMiddlewareAsync(failure);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(ApiExceptionResults.ConcurrencyMessage, Message(body));
        Assert.DoesNotContain("row(s)", body);
    }

    [Fact]
    public async Task EndpointExecution_GenericInvalidOperation_Returns500WithClientTitleOnly()
    {
        var result = await EndpointExecution.RunAsync(
            () => throw new InvalidOperationException("Login did not provide original authentication time."),
            NullLogger.Instance, "Falha", "Erro ao autenticar usuario");

        var (status, body) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("Erro ao autenticar usuario", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("authentication time", body);
    }

    [Fact]
    public async Task EndpointExecution_BusinessRule_KeepsMessage()
    {
        var result = await EndpointExecution.RunAsync(
            () => throw new BusinessRuleException("Email ja cadastrado"),
            NullLogger.Instance, "Falha", "Erro ao criar usuario");

        var (status, body) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("Email ja cadastrado", Message(body));
    }

    [Fact]
    public void ExistingSecurityExceptions_RemainExpected()
    {
        Assert.True(ApiExceptionResults.IsExpected(new HemodinksAPI.Application.Security.CompromisedPasswordException()));
        Assert.True(ApiExceptionResults.IsExpected(new HemodinksAPI.Application.Security.SensitiveIdentityException("x", "y")));
        Assert.False(ApiExceptionResults.IsExpected(new InvalidOperationException("internal")));
        Assert.False(ApiExceptionResults.IsExpected(new KeyNotFoundException("internal")));
    }

    private static InvalidOperationException CaptureEfTrackingFailure()
    {
        using var db = new ProbeContext();
        db.Attach(new ProbeEntity { Id = 1 });
        return Assert.Throws<InvalidOperationException>(() => db.Attach(new ProbeEntity { Id = 1 }));
    }

    private static string? Message(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();

    private static async Task<(int Status, string Body, LogLevel? Level)> InvokeMiddlewareAsync(Exception failure)
    {
        var logger = new CaptureLogger();
        var context = CreateContext();
        var middleware = new ApiExceptionHandlingMiddleware(_ => throw failure, logger);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), logger.Level);
    }

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = CreateContext();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
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

    private sealed class ProbeEntity
    {
        public int Id { get; set; }
    }

    private sealed class ProbeContext : DbContext
    {
        public DbSet<ProbeEntity> Entities => Set<ProbeEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }

    private sealed class CaptureLogger : ILogger<ApiExceptionHandlingMiddleware>
    {
        public LogLevel? Level { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Level = logLevel;
    }
}
