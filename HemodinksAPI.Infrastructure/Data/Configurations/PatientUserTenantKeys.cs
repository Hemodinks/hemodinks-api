using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal static class PatientUserTenantKeys
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasAlternateKey(item => new { item.ClinicaId, item.Id });
        modelBuilder.Entity<Paciente>().HasAlternateKey(item => new { item.ClinicaId, item.Id });

        // Include the clinic in every tenant-owned reference to a patient or user,
        // including files, observations, doctors and clinic memberships.
        var foreignKeys = modelBuilder.Model.GetEntityTypes()
            .Where(entity => typeof(IClinicaOwnedEntity).IsAssignableFrom(entity.ClrType))
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(User)
                || key.PrincipalEntityType.ClrType == typeof(Paciente))
            .ToList();

        foreach (var foreignKey in foreignKeys)
        {
            var clinicProperty = foreignKey.DeclaringEntityType.FindProperty(nameof(IClinicaOwnedEntity.ClinicaId))!;
            var principal = foreignKey.PrincipalEntityType;
            var principalKey = principal.FindKey([
                principal.FindProperty(nameof(IClinicaOwnedEntity.ClinicaId))!,
                principal.FindProperty(nameof(User.Id))!
            ])!;
            foreignKey.SetProperties([clinicProperty, .. foreignKey.Properties], principalKey);
        }
    }
}
