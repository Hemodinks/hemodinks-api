using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> entity)
    {
        entity.ToTable("PasswordResetTokens");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(e => e.ExpiresAt)
            .IsRequired();

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.RequestIp)
            .HasMaxLength(45);

        entity.HasIndex(e => e.TokenHash)
            .IsUnique();

        entity.HasIndex(e => new { e.UserId, e.UsedAt, e.ExpiresAt });

        entity.HasOne(e => e.User)
            .WithMany(e => e.PasswordResetTokens)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
