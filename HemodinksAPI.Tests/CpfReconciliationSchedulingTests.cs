using System.Collections.Concurrent;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

// O reconciliador de CPF so pode tocar o banco depois que trafego real o acordou, e indisponibilidade
// passageira vira nova tentativa (Warning), nao Error. Logs nunca carregam mensagem de excecao transitoria.
public sealed class CpfReconciliationSchedulingTests
{
    private const string Cpf = "52998224725";
    private static readonly CpfReconciliationSchedule NoDelays = new(TimeSpan.Zero, [TimeSpan.Zero, TimeSpan.Zero]);

    [Fact]
    public async Task Does_not_touch_the_database_until_it_is_available()
    {
        await using var harness = await Harness.CreateAsync();
        var service = harness.CreateService(NoDelays);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        Assert.Equal(0, harness.ContextResolutions);
        Assert.False(service.ExecuteTask!.IsCompleted);

        harness.Signal.MarkAvailable();
        await service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, harness.ContextResolutions);
        Assert.Equal(TestCpfProtection.Instance.ComputeLookupHash(Cpf), await harness.ReadHashAsync());
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level >= LogLevel.Warning && !entry.IsCompletionMilestone);
        var completed = Assert.Single(harness.Logger.Entries, entry => entry.IsCompletionMilestone);
        Assert.Equal(LogLevel.Warning, completed.Level);
        Assert.StartsWith("Reconciliacao de CPF concluida", completed.Message);
        Assert.DoesNotContain(Cpf, completed.Message);
    }

    [Fact]
    public async Task Transient_unavailability_retries_with_warning_and_no_sensitive_text()
    {
        await using var harness = await Harness.CreateAsync(failures: 1, failure: () => new TimeoutException("secret connection details"));
        harness.Signal.MarkAvailable();
        var service = harness.CreateService(NoDelays);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, harness.ContextResolutions);
        Assert.Equal(TestCpfProtection.Instance.ComputeLookupHash(Cpf), await harness.ReadHashAsync());
        var warning = Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning && !entry.IsCompletionMilestone);
        Assert.Contains(nameof(TimeoutException), warning.Message);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Message.Contains("secret") || entry.Message.Contains(Cpf));
    }

    [Fact]
    public async Task Persistent_unavailability_gives_up_with_a_single_error_after_the_retries()
    {
        await using var harness = await Harness.CreateAsync(failures: int.MaxValue,
            failure: () => new InvalidOperationException("wrapper", new TimeoutException("secret")));
        harness.Signal.MarkAvailable();
        var service = harness.CreateService(NoDelays);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, harness.ContextResolutions);
        Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        var error = Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("Falha na reconciliacao da protecao de CPF", error.Message);
        Assert.Null(await harness.ReadHashAsync());
    }

    [Fact]
    public async Task Non_transient_failure_is_not_retried()
    {
        await using var harness = await Harness.CreateAsync(failures: int.MaxValue,
            failure: () => new InvalidOperationException("bug"));
        harness.Signal.MarkAvailable();
        var service = harness.CreateService(NoDelays);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, harness.ContextResolutions);
        Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Stopping_while_waiting_for_the_database_ends_quietly()
    {
        await using var harness = await Harness.CreateAsync();
        var service = harness.CreateService(NoDelays);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompleted);
        Assert.Equal(0, harness.ContextResolutions);
        Assert.Empty(harness.Logger.Entries);
    }

    [Fact]
    public async Task Stopping_during_the_retry_wait_ends_without_error()
    {
        await using var harness = await Harness.CreateAsync(failures: int.MaxValue);
        harness.Signal.MarkAvailable();
        var service = harness.CreateService(new(TimeSpan.Zero, [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)]));

        await service.StartAsync(CancellationToken.None);
        for (var i = 0; i < 100 && harness.ContextResolutions == 0; i++) await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompleted);
        Assert.Equal(1, harness.ContextResolutions);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Real_connection_failure_is_classified_as_unavailability_with_matched_type()
    {
        // Nada escuta na porta 1: o TCP e recusado antes do TLS, sem chamada externa.
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
            "Server=tcp:127.0.0.1,1;Database=x;User Id=u;Password=Not-A-Real-Password;Connect Timeout=2;Encrypt=True");
        var sql = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => connection.OpenAsync());

        Assert.True(CpfProtectionReconciliationService.TryGetUnavailability(
            new InvalidOperationException("wrapper", sql), out var cause, out var number));
        Assert.Same(sql, cause);
        Assert.Equal(sql.Number, number);

        Assert.False(CpfProtectionReconciliationService.TryGetUnavailability(
            new InvalidOperationException("bug"), out _, out var none));
        Assert.Null(none);
    }

    [Fact]
    public async Task Disabled_reconciliation_never_waits_or_touches_the_database()
    {
        await using var harness = await Harness.CreateAsync();
        var service = harness.CreateService(NoDelays, reconcileOnStartup: false);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, harness.ContextResolutions);
    }

    [Fact]
    public void Default_schedule_waits_before_running_and_retries_twice()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), CpfReconciliationSchedule.Default.StartDelay);
        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)], CpfReconciliationSchedule.Default.RetryDelays);
    }

    [Fact]
    public void Successful_open_marks_the_database_available_and_a_failed_open_does_not()
    {
        var signal = new DatabaseAvailabilitySignal();
        var interceptor = new DatabaseConnectionTimingInterceptor(new CapturingLogger<DatabaseConnectionTimingInterceptor>(), signal);

        interceptor.RecordFailed(TimeSpan.FromSeconds(29), new TimeoutException(), "PlatformDbContext");
        Assert.False(signal.IsAvailable);

        interceptor.RecordOpened(TimeSpan.FromMilliseconds(20), "AppDbContext");
        Assert.True(signal.IsAvailable);
    }

    [Fact]
    public async Task Readiness_probe_marks_available_only_after_the_query_succeeds()
    {
        var signal = new DatabaseAvailabilitySignal();
        var failing = new SqlDatabaseReadinessProbe(
            new TestingSqlConnectionFactory(() => throw new TimeoutException()),
            () => throw new InvalidOperationException("no EF"), availability: signal);
        await Assert.ThrowsAsync<TimeoutException>(() => failing.CheckAsync(false, CancellationToken.None));
        Assert.False(signal.IsAvailable);

        var path = Path.Combine(Path.GetTempPath(), $"cpf-signal-{Guid.NewGuid():N}.db");
        try
        {
            var working = new SqlDatabaseReadinessProbe(
                new TestingSqlConnectionFactory(() => new SqliteConnection($"Data Source={path};Pooling=False")),
                () => throw new InvalidOperationException("no EF"), availability: signal);
            Assert.True((await working.CheckAsync(false, CancellationToken.None)).Connected);
            Assert.True(signal.IsAvailable);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Database_and_cpf_registrations_share_one_signal()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=tcp:127.0.0.1,1;Database=x;User Id=u;Password=p;Encrypt=False",
            ["Warmup:DataModelOnStartup"] = "false"
        }).Build();
        services.AddCpfProtection(configuration, new TestingEnvironment());
        HemodinksAPI.Api.ApiServiceCollectionExtensions.AddDatabase(services, configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DatabaseConnectionTimingInterceptor>().RecordOpened(TimeSpan.Zero, "AppDbContext");

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(DatabaseAvailabilitySignal));
        Assert.True(provider.GetRequiredService<DatabaseAvailabilitySignal>().IsAvailable);
        Assert.Same(CpfReconciliationSchedule.Default, provider.GetRequiredService<CpfReconciliationSchedule>());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private int _resolutions;

        private Harness(SqliteConnection connection, int failures, Func<Exception> failure)
        {
            _connection = connection;
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var services = new ServiceCollection();
            services.AddSingleton(TestCpfProtection.Instance);
            services.AddScoped(_ =>
            {
                // Cada tentativa resolve o contexto: as primeiras falham como o SQL serverless retomando.
                var attempt = Interlocked.Increment(ref _resolutions);
                if (attempt <= failures) throw failure();
                return new PlatformDbContext(options, TestCpfProtection.Instance);
            });
            _provider = services.BuildServiceProvider();
        }

        public DatabaseAvailabilitySignal Signal { get; } = new();
        public CapturingLogger<CpfProtectionReconciliationService> Logger { get; } = new();
        public int ContextResolutions => Volatile.Read(ref _resolutions);

        public static async Task<Harness> CreateAsync(int failures = 0, Func<Exception>? failure = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, ClinicaId INTEGER NOT NULL,
                    Nome TEXT, Email TEXT, Telefone TEXT, Senha TEXT, PerfilId INTEGER NOT NULL DEFAULT 2,
                    Ativo INTEGER NOT NULL DEFAULT 1, PrecisaTrocarSenha INTEGER NOT NULL DEFAULT 1,
                    DataCadastro TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, DataAtualizacao TEXT, DataNascimento TEXT,
                    Crm TEXT, CrmUf TEXT, FotoPerfil TEXT, Cpf TEXT, CpfCifrado TEXT, CpfHash TEXT);
                INSERT INTO Users (Id, ClinicaId, Cpf) VALUES (1, 1, '52998224725');
                """;
            await command.ExecuteNonQueryAsync();
            return new Harness(connection, failures, failure ?? (() => new TimeoutException()));
        }

        public CpfProtectionReconciliationService CreateService(CpfReconciliationSchedule schedule, bool reconcileOnStartup = true) =>
            new(_provider.GetRequiredService<IServiceScopeFactory>(),
                new CpfProtectionRuntimeInfo(UsesEphemeralKeys: false, ReconcileOnStartup: reconcileOnStartup),
                Signal, schedule, Logger);

        public async Task<string?> ReadHashAsync()
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT CpfHash FROM Users WHERE Id = 1";
            return await command.ExecuteScalarAsync() as string;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, int EventId = 0)
    {
        // #277: a conclusao e um Warning proposital (encaminhado ao New Relic), nao um alerta.
        public bool IsCompletionMilestone => EventId == CpfProtectionReconciliationService.CpfReconciliationCompletedEvent.Id;
    }

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();
        public IReadOnlyList<LogEntry> Entries => [.. _entries];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Enqueue(new(logLevel, formatter(state, exception), exception, eventId.Id));
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "HemodinksAPI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
