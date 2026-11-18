using System.Collections.Concurrent;
using System.Data.Common;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public sealed class DataModelWarmupTests
{
    // Nada escuta na porta 1: se o aquecimento tentasse conectar, o interceptor registraria.
    private const string UnreachableSqlServer =
        "Server=tcp:127.0.0.1,1;Database=HemodinksWarmup;User Id=probe;Password=Not-A-Real-Password;Connect Timeout=1;Encrypt=False";

    [Fact]
    public async Task Builds_models_of_both_contexts_without_opening_a_connection()
    {
        var connections = new ConnectionAttemptCounter();
        var logger = new CapturingLogger();
        await using var provider = BuildProvider(connections);

        var service = new DataModelWarmupHostedService(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(0, connections.Attempts);
        Assert.StartsWith("DataModelWarmupCompleted", Assert.Single(logger.Messages));
        Assert.Equal(2, connections.ContextsCreated.Distinct().Count());
    }

    [Fact]
    public async Task Failure_is_swallowed_and_logs_only_the_error_type()
    {
        var logger = new CapturingLogger();
        var services = new ServiceCollection();
        services.AddScoped<PlatformDbContext>(_ => throw new InvalidOperationException("secret connection details"));
        await using var provider = services.BuildServiceProvider();

        var service = new DataModelWarmupHostedService(provider, logger);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(logger.Messages);
        Assert.StartsWith("DataModelWarmupFailed", message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.DoesNotContain("secret", message);
    }

    [Fact]
    public async Task Cancelled_warmup_resolves_no_context()
    {
        var connections = new ConnectionAttemptCounter();
        await using var provider = BuildProvider(connections);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => DataModelWarmupHostedService.WarmUp(provider, cancelled.Token));
        Assert.Empty(connections.ContextsCreated);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Registration_follows_configuration(string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = UnreachableSqlServer,
            ["Warmup:DataModelOnStartup"] = setting
        }).Build();
        var services = new ServiceCollection();

        HemodinksAPI.Api.ApiServiceCollectionExtensions.AddDatabase(services, configuration);

        Assert.Equal(expected, services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(DataModelWarmupHostedService)));
    }

    private static ServiceProvider BuildProvider(ConnectionAttemptCounter counter)
    {
        var services = new ServiceCollection();
        services.AddScoped<IClinicaContext>(_ => ClinicaContextFactory.CreateDefaultResolved());
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(UnreachableSqlServer).AddInterceptors(counter));
        services.AddScoped<PlatformDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class ConnectionAttemptCounter : DbConnectionInterceptor, IQueryExpressionInterceptor
    {
        private int _attempts;
        public int Attempts => _attempts;
        public ConcurrentBag<string> ContextsCreated { get; } = [];

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            Interlocked.Increment(ref _attempts);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            return ValueTask.FromResult(result);
        }

        // Chamado na traducao LINQ -> SQL (inclusive ToQueryString): prova que cada contexto montou o modelo.
        public System.Linq.Expressions.Expression QueryCompilationStarting(
            System.Linq.Expressions.Expression queryExpression, QueryExpressionEventData eventData)
        {
            ContextsCreated.Add(eventData.Context!.GetType().Name);
            return queryExpression;
        }
    }

    private sealed class CapturingLogger : ILogger<DataModelWarmupHostedService>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }
}
