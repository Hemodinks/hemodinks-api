using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class TeamChallengeConcurrencyTests
{
    [Fact]
    public async Task ConcurrentConsumers_OnlyOneCanMarkChallengeUsed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var first = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        await using var second = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        // Production model/concurrency configuration; only this table is needed for the contested update.
        await first.Database.ExecuteSqlRawAsync("""
            CREATE TABLE EquipeLoginDesafios (Id INTEGER PRIMARY KEY, ClinicaId INTEGER, EquipeId INTEGER,
                TokenHash TEXT, SecurityVersion TEXT, DataCadastro TEXT, ExpiraEm TEXT, UtilizadoEm TEXT, RequestIp TEXT);
            INSERT INTO EquipeLoginDesafios VALUES (1, 1, 0, 'test-hash', '00000000-0000-0000-0000-000000000000',
                '2030-01-01 00:00:00', '2030-01-01 00:05:00', NULL, NULL);
            """);
        var a = await first.EquipeLoginDesafios.SingleAsync();
        var b = await second.EquipeLoginDesafios.SingleAsync();
        a.UtilizadoEm = b.UtilizadoEm = DateTime.UtcNow;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
