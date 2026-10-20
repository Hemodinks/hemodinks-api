using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HemodinksAPI.Tests;

public sealed class EventCreatorMigrationTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task CreatorMigration_PreservesLegacyUnknownCreatorAndRejectsCrossClinicAtDatabaseBoundary()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1")
            Assert.Skip("Set HEMODINKS_TEST_LOCALDB=1 to test the additive creator migration in a disposable SQL Server database.");
        var name = $"HemodinksEventCreatorAudit_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(SqlServerTestConnection.Create(name)).Options;
        await using var db = new PlatformDbContext(options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var local = await AgendaSecurityTests.AddUser(db, "local", Perfil.AdministradorId);
            var clinic = new Clinica { Nome = "Other", Slug = "other-creator" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            var remote = await AgendaSecurityTests.AddUser(db, "remote", Perfil.AdministradorId, clinic.Id);
            var ev = new Event { ClinicaId = 1, UserId = local.Id, Title = "Legacy event", Start = DateTime.UtcNow,
                End = DateTime.UtcNow.AddHours(1), CreatedByUserId = null };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            var migration = new AddEventAuthenticatedCreator();
            await Execute(db, migration.DownOperations);
            await Execute(db, migration.UpOperations);
            db.ChangeTracker.Clear();
            var legacy = await db.Events.SingleAsync(x => x.Id == ev.Id);
            Assert.Null(legacy.CreatedByUserId);
            Assert.Equal("Legacy event", legacy.Title);
            Assert.Equal(local.Id, legacy.UserId);
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Events SET CreatedByUserId = {remote.Id} WHERE Id = {ev.Id}"));
            Assert.Equal(547, error.Number);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Events SET CreatedByUserId = {local.Id} WHERE Id = {ev.Id}");
            Assert.Equal(local.Id, (await db.Events.AsNoTracking().SingleAsync(x => x.Id == ev.Id)).CreatedByUserId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task Execute(AppDbContext db, IReadOnlyList<MigrationOperation> operations)
    {
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }
}
