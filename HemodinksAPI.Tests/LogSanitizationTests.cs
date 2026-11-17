using HemodinksAPI.Api;
using HemodinksAPI.Application.Idempotency;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

// #328: valores da requisição entram no log sem quebra de linha, e o e-mail usado como escopo de
// idempotência não aparece no log.
public sealed class LogSanitizationTests
{
    [Theory]
    [InlineData("/api/users\r\n2026-11-17 [INF] linha forjada", "/api/users??2026-11-17 [INF] linha forjada")]
    [InlineData("/api/x\u2028y\u2029z\u0000", "/api/x?y?z?")]
    [InlineData("/api/pacientes/42", "/api/pacientes/42")]
    [InlineData(null, "")]
    public void Clean_ReplacesLineBreaksAndControlCharacters(string? value, string expected) =>
        Assert.Equal(expected, LogSanitizer.Clean(value));

    [Fact]
    public async Task ExceptionMiddleware_LogsPathWithoutLineBreaks()
    {
        var logger = new CaptureLogger<ApiExceptionHandlingMiddleware>();
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Request.Method = "GET";
        context.Request.Path = "/api/x\n2026-11-17 [ERR] forjado";
        context.Response.Body = new MemoryStream();

        await new ApiExceptionHandlingMiddleware(_ => throw new Exception("falha"), logger).InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain('\n', message);
        Assert.Contains("/api/x?2026-11-17 [ERR] forjado", message);
    }

    [Fact]
    public async Task IdempotencyCleanupFailure_DoesNotLogTheEmailScope()
    {
        const string email = "pessoa.real@example.com";
        var logger = new CaptureLogger<RequestIdempotencyService>();
        var service = new RequestIdempotencyService(new FailingRemoveStore(), new FixedClinic(), logger, TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestIdempotencyService.IdempotencyKeyHeaderName] = "chave-328";

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync<object>(
            context, "users.password-reset.request", email, new { email }, StatusCodes.Status202Accepted,
            _ => throw new InvalidOperationException("falha na acao"), CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("users.password-reset.request", message);
        Assert.DoesNotContain("pessoa.real", message);
        Assert.DoesNotContain("example.com", message);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class FixedClinic : IClinicaContext
    {
        public int? ClinicaId => 1;
        public string? ClinicaSlug => "clinica-a";
        public bool IsResolved => true;
        public bool IsPlatformScope => false;
        public int GetRequiredClinicaId() => 1;
    }

    private sealed class FailingRemoveStore : IIdempotencyRequestStore
    {
        public Task<IdempotencyRequest?> FindAsync(int clinicaId, string operation, string scope, string key,
            CancellationToken cancellationToken) => Task.FromResult<IdempotencyRequest?>(null);

        public Task<bool> TryAddAsync(IdempotencyRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task CompleteAsync(IdempotencyRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveAsync(IdempotencyRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("banco indisponivel");
    }
}
