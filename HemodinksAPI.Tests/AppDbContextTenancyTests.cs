using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;

namespace HemodinksAPI.Tests;

public class AppDbContextTenancyTests
{
    [Fact]
    public void OnModelCreating_AppliesClinicaQueryFilterToAllClinicaOwnedEntities()
    {
        using var context = TestDbContextFactory.Create();

        var unfilteredClinicaOwnedEntities = context.Model
            .GetEntityTypes()
            .Where(entityType => typeof(IClinicaOwnedEntity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType => !entityType.GetDeclaredQueryFilters().Any())
            .Select(entityType => entityType.ClrType.Name)
            .Order()
            .ToList();

        Assert.Empty(unfilteredClinicaOwnedEntities);
    }

    [Fact]
    public void OnModelCreating_AllClinicaOwnedEntitiesHaveClinicForeignKeyAndLeadingIndex()
    {
        using var context = TestDbContextFactory.Create();

        var invalidEntities = context.Model.GetEntityTypes()
            .Where(entityType => typeof(IClinicaOwnedEntity).IsAssignableFrom(entityType.ClrType))
            .Where(entityType =>
                !entityType.GetForeignKeys().Any(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Clinica))
                || !entityType.GetIndexes().Any(index => index.Properties.FirstOrDefault()?.Name == nameof(IClinicaOwnedEntity.ClinicaId)))
            .Select(entityType => entityType.ClrType.Name)
            .Order()
            .ToList();

        Assert.Empty(invalidEntities);
    }

    [Fact]
    public async Task QueryWithoutResolvedClinic_IsFailClosed()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var platformContext = new ClinicaContext();
        platformContext.SetPlatformScope();

        await using (var seedContext = new AppDbContext(options, platformContext))
        {
            seedContext.Users.Add(CreateUser(Clinica.DefaultId, "tenant@example.com"));
            await seedContext.SaveChangesAsync();
        }

        await using var unresolvedContext = new AppDbContext(options, new ClinicaContext());
        Assert.Empty(await unresolvedContext.Users.ToListAsync());
    }

    [Fact]
    public async Task SaveChanges_WithDivergentClinicaId_IsRejected()
    {
        var selected = new ClinicaContext();
        selected.SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options, selected);
        context.Users.Add(CreateUser(2, "wrong-clinic@example.com"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("ClinicaId divergente", exception.Message);
    }

    [Fact]
    public async Task SaveChanges_WithCrossTenantRelationship_IsRejected()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var platform = new ClinicaContext();
        platform.SetPlatformScope();
        int otherClinicUserId;

        await using (var seedContext = new AppDbContext(options, platform))
        {
            seedContext.Clinicas.Add(new Clinica { Id = 2, Nome = "Outra", Slug = "outra" });
            var otherUser = CreateUser(2, "other@example.com");
            seedContext.Users.Add(otherUser);
            await seedContext.SaveChangesAsync();
            otherClinicUserId = otherUser.Id;
        }

        var selected = new ClinicaContext();
        selected.SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        await using var context = new AppDbContext(options, selected);
        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            ClinicaId = Clinica.DefaultId,
            UserId = otherClinicUserId,
            TokenHash = "hash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Relacionamento entre clinicas diferentes", exception.Message);
    }

    private static User CreateUser(int clinicaId, string email)
    {
        return new User
        {
            ClinicaId = clinicaId,
            Nome = email,
            Email = email,
            Telefone = "+5511999999999",
            Senha = "hash",
            PerfilId = Perfil.AdministradorId,
            Ativo = true
        };
    }
}
