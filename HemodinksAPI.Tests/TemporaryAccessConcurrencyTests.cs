using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class TemporaryAccessConcurrencyTests
{
    // Uses the production mappings, and a relational provider so the losing transaction rolls back.
    private sealed class RecoveryDb(DbContextOptions<RecoveryDb> options) : DbContext(options)
    {
        public DbSet<TemporaryAccessCredential> Credentials => Set<TemporaryAccessCredential>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<UsuarioGlobal>().Ignore(x => x.Clinicas);
            builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly, type =>
                type.Name is "UsuarioGlobalConfiguration" or "TemporaryAccessCredentialConfiguration");
        }
    }

    [Fact]
    public async Task TwoConcurrentConsumers_OnlyOneCommits_AndGenerationAlsoConflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RecoveryDb>().UseSqlite(connection).Options;
        await using var first = new RecoveryDb(options);
        await first.Database.EnsureCreatedAsync();
        first.Credentials.Add(new TemporaryAccessCredential
        {
            Id = Guid.NewGuid(), UserId = 7, ClinicaId = 1, PasswordHash = "hash-only", CreatedByUserId = 1,
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            UsuarioGlobal = new UsuarioGlobal { Nome = "Teste", Email = "test@example.com", Senha = "hash-only", SecurityVersion = Guid.NewGuid() }
        });
        await first.SaveChangesAsync();
        first.ChangeTracker.Clear();
        await using var second = new RecoveryDb(options);
        await using var generator = new RecoveryDb(options);
        var a = await first.Credentials.Include(x => x.UsuarioGlobal).SingleAsync();
        var b = await second.Credentials.Include(x => x.UsuarioGlobal).SingleAsync();
        var replacement = await generator.Credentials.Include(x => x.UsuarioGlobal).SingleAsync();
        // Both logins have validated the same unused credential before either commits.
        a.UsedAtUtc = b.UsedAtUtc = DateTime.UtcNow;
        a.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
        b.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        replacement.Id = Guid.NewGuid();
        replacement.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => generator.SaveChangesAsync());
        await using var verify = new RecoveryDb(options);
        var saved = await verify.Credentials.Include(x => x.UsuarioGlobal).SingleAsync();
        Assert.Equal(a.UsuarioGlobal.SecurityVersion, saved.UsuarioGlobal.SecurityVersion);
        Assert.Equal(a.Id, saved.Id);
        Assert.NotNull(saved.UsedAtUtc);
    }
}
