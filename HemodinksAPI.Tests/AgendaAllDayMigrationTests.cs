using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace HemodinksAPI.Tests;

public class AgendaAllDayMigrationTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task NewMigrationPreservesTimedRowsAndRoundTripsCivilDates()
    {
        var name = $"HemodinksAllDay_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(SqlServerTestConnection.Create(name)).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        try
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE Events (Id int NOT NULL PRIMARY KEY, ClinicaId int NOT NULL, Start datetime2 NOT NULL, [End] datetime2 NOT NULL); INSERT INTO Events VALUES (1,1,'2033-09-26T12:00:00','2033-09-26T13:00:00');");
            var migration = new AddAllDayEvents();
            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model);
            foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
            var old = await db.Events.Select(ev => new { ev.IsAllDay, ev.Start, ev.AllDayStartDate, ev.TimeZoneId }).SingleAsync();
            Assert.False(old.IsAllDay); Assert.Null(old.AllDayStartDate); Assert.Null(old.TimeZoneId);
            Assert.Equal(new DateTime(2033, 9, 26, 12, 0, 0, DateTimeKind.Utc), old.Start);
            await db.Database.ExecuteSqlRawAsync("UPDATE Events SET IsAllDay=1, AllDayStartDate='2033-09-26', AllDayEndDate='2033-09-28', TimeZoneId='America/Sao_Paulo', Start='2033-09-26T03:00:00', [End]='2033-09-29T03:00:00' WHERE Id=1;");
            var current = await db.Events.Select(ev => new { ev.IsAllDay, ev.AllDayStartDate, ev.AllDayEndDate, ev.TimeZoneId, ev.End }).SingleAsync();
            Assert.True(current.IsAllDay); Assert.Equal(new DateOnly(2033, 9, 26), current.AllDayStartDate);
            Assert.Equal(new DateOnly(2033, 9, 28), current.AllDayEndDate); Assert.Equal("America/Sao_Paulo", current.TimeZoneId);
            Assert.Equal(DateTimeKind.Utc, current.End.Kind);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Events SET AllDayEndDate='2033-09-25' WHERE Id=1;"));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
