using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class UserPrivacyPreferenceConfiguration : IEntityTypeConfiguration<UserPrivacyPreference>
{
    public void Configure(EntityTypeBuilder<UserPrivacyPreference> entity)
    {
        entity.ToTable("UserPrivacyPreferences");
        entity.HasKey(item => item.Id);

        entity.Property(item => item.DocumentVersion)
            .HasMaxLength(20)
            .IsRequired();
        entity.Property(item => item.AcceptedAtUtc).IsRequired();
        entity.Property(item => item.UpdatedAtUtc).IsRequired();

        entity.HasIndex(item => new { item.ClinicaId, item.UserId }).IsUnique();

        entity.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Clinica)
            .WithMany()
            .HasForeignKey(item => item.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
