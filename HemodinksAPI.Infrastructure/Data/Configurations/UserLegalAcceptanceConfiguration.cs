using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class UserLegalAcceptanceConfiguration : IEntityTypeConfiguration<UserLegalAcceptance>
{
    public void Configure(EntityTypeBuilder<UserLegalAcceptance> entity)
    {
        entity.ToTable("UserLegalAcceptances");
        entity.HasKey(item => item.Id);

        entity.Property(item => item.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(item => item.DocumentVersion)
            .HasMaxLength(20)
            .IsRequired();
        entity.Property(item => item.AcceptedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(item => new
        {
            item.ClinicaId,
            item.UserId,
            item.DocumentType,
            item.DocumentVersion
        }).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.UserId, item.AcceptedAtUtc });

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
