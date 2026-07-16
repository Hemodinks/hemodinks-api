using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

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
}
