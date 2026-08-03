using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AuthenticationSessionConfiguration : IEntityTypeConfiguration<AuthenticationSession>
{
    public void Configure(EntityTypeBuilder<AuthenticationSession> entity)
    {
        entity.ToTable("AuthenticationSessions");
        entity.HasKey(item => item.Id);

        entity.Property(item => item.RefreshTokenHash)
            .IsRequired()
            .HasMaxLength(64);
        entity.Property(item => item.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");
        entity.Property(item => item.LastActivityAt).IsRequired();
        entity.Property(item => item.CreatedByIp).HasMaxLength(45);
        entity.Property(item => item.UserAgent).HasMaxLength(512);
        entity.Property(item => item.RowVersion).IsRowVersion();

        entity.HasIndex(item => item.RefreshTokenHash).IsUnique();
        entity.HasIndex(item => new { item.UsuarioGlobalId, item.RevokedAt, item.LastActivityAt });
        entity.HasIndex(item => item.UsuarioClinicaId);

        entity.HasOne(item => item.UsuarioGlobal)
            .WithMany()
            .HasForeignKey(item => item.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.UsuarioClinica)
            .WithMany()
            .HasForeignKey(item => item.UsuarioClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
