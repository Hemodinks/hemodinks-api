using System.Collections.Concurrent;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

/// <summary>Erro de gravacao de usuario nao leva CPF nem email ao log (#227).</summary>
public sealed class UserPersistenceErrorLoggingTests
{
    private const string Cpf = "52998224725";

    [Fact]
    public async Task Sanitized_user_error_drops_the_sql_message_and_keeps_only_the_constraint_name()
    {
        await using var connection = await OpenDatabase();
        await using var context = CreateContext(connection, new CapturingLoggerProvider());
        var user = NewUser(1, "a@example.com");
        context.Users.Add(user);
        var raw = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException(
                $"Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Users_ClinicaId_Cpf'. The duplicate key value is (1, {Cpf}). a@example.com"),
            [context.Entry(user)]);

        Assert.True(UserPersistenceErrors.ShouldSanitize(raw, context.ChangeTracker));
        var sanitized = UserPersistenceErrors.Sanitize(raw);

        Assert.Null(sanitized.InnerException);
        Assert.DoesNotContain(Cpf, sanitized.ToString());
        Assert.DoesNotContain("a@example.com", sanitized.ToString());
        Assert.Contains("IX_Users_ClinicaId_Cpf", sanitized.Message);
        Assert.Same(user, Assert.Single(sanitized.Entries).Entity);
    }

    [Fact]
    public async Task Errors_without_user_changes_and_concurrency_errors_are_not_rewritten()
    {
        await using var connection = await OpenDatabase();
        await using var context = CreateContext(connection, new CapturingLoggerProvider());

        Assert.False(UserPersistenceErrors.ShouldSanitize(
            new DbUpdateException("outra tabela", new InvalidOperationException("detalhe")), context.ChangeTracker));

        var user = NewUser(1, "a@example.com");
        context.Users.Add(user);
        Assert.False(UserPersistenceErrors.ShouldSanitize(
            new DbUpdateConcurrencyException("concorrencia"), context.ChangeTracker));
    }

    [Fact]
    public async Task Unavailable_database_error_on_user_save_is_not_rewritten_and_still_maps_to_503()
    {
        await using var unreachable = new SqlConnection(
            "Server=tcp:127.0.0.1,1;Database=Indisponivel;User Id=probe;Password=probe;Connect Timeout=2;Encrypt=False");
        var sqlError = await Assert.ThrowsAsync<SqlException>(() => unreachable.OpenAsync());
        Assert.True(SqlAvailabilityErrors.IsUnavailable(sqlError.Number, sqlError.Class));

        await using var connection = await OpenDatabase();
        await using var context = CreateContext(connection, new CapturingLoggerProvider());
        var user = NewUser(1, "a@example.com");
        context.Users.Add(user);
        var raw = new DbUpdateException("falha", sqlError, [context.Entry(user)]);

        Assert.False(UserPersistenceErrors.ShouldSanitize(raw, context.ChangeTracker));
        Assert.True(SqlAvailabilityErrors.IsTransient(1205, 13));
        Assert.False(SqlAvailabilityErrors.IsTransient(2601, 14));
    }

    [Fact]
    public async Task Duplicate_user_in_same_clinic_throws_without_inner_exception_and_ef_does_not_log_it()
    {
        await using var connection = await OpenDatabase();
        var logs = new CapturingLoggerProvider();
        await using (var context = CreateContext(connection, logs))
        {
            context.Users.Add(NewUser(1, "a@example.com"));
            await context.SaveChangesAsync();
        }

        await using var duplicate = CreateContext(connection, logs);
        duplicate.Users.Add(NewUser(1, "b@example.com"));
        var asyncError = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        var syncError = Assert.Throws<DbUpdateException>(() => duplicate.SaveChanges());

        foreach (var error in new[] { asyncError, syncError })
        {
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("UNIQUE constraint failed", error.ToString());
            Assert.StartsWith("Falha ao gravar usuario", error.Message);
        }

        // Nenhum log visivel com nivel >= Information carrega a excecao bruta do banco.
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Level >= LogLevel.Information
            && (entry.Exception != null || entry.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal)));
    }

    private static User NewUser(int clinicaId, string email) => new()
    {
        ClinicaId = clinicaId,
        Nome = "Usuario CPF",
        Email = email,
        Telefone = "+5511999990000",
        Cpf = Cpf,
        Senha = "hash",
        PerfilId = Perfil.MedicosId
    };

    private static async Task<SqliteConnection> OpenDatabase()
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
            CREATE UNIQUE INDEX IX_Users_ClinicaId_Cpf ON Users (ClinicaId, Cpf) WHERE Cpf IS NOT NULL;
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static AppDbContext CreateContext(SqliteConnection connection, CapturingLoggerProvider logs)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .UseLoggerFactory(LoggerFactory.Create(logging => logging
                .SetMinimumLevel(LogLevel.Information)
                .AddProvider(logs)));
        builder.SuppressRawDatabaseErrorLogs();
        return new AppDbContext(builder.Options, ClinicaContextFactory.CreatePlatform(), TestCpfProtection.Instance);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}
