using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class IdempotencyRequestConfiguration : IEntityTypeConfiguration<IdempotencyRequest>
{
    public void Configure(EntityTypeBuilder<IdempotencyRequest> entity)
    {
        entity.ToTable("IdempotencyRequests");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.Operation)
            .IsRequired()
            .HasMaxLength(120);

        entity.Property(e => e.Scope)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(e => e.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(e => e.RequestHash)
            .IsRequired()
            .HasMaxLength(64);

        entity.Property(e => e.State)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ResourceLocation)
            .HasMaxLength(512);

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.CompletedAt);

        entity.HasIndex(e => new { e.ClinicaId, e.Operation, e.Scope, e.IdempotencyKey })
            .IsUnique();

        entity.HasIndex(e => e.CreatedAt);

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
