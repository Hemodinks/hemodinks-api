using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class EventEmailDeliveryConfiguration : IEntityTypeConfiguration<EventEmailDelivery>
{
    public void Configure(EntityTypeBuilder<EventEmailDelivery> entity)
    {
        entity.ToTable("EventEmailDeliveries");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.RecipientEmail).IsRequired().HasMaxLength(320);
        entity.Property(item => item.Status).IsRequired().HasMaxLength(16);
        entity.HasIndex(item => new { item.ClinicaId, item.EventId, item.RecipientUserId }).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.EventId, item.RecipientEmail }).IsUnique();
        entity.HasIndex(item => new { item.Status, item.NextAttemptAt, item.LeaseUntil });
        entity.HasOne(item => item.Event).WithMany().HasForeignKey(item => item.EventId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.RecipientUser).WithMany().HasForeignKey(item => item.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.SenderUser).WithMany().HasForeignKey(item => item.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Clinica>().WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
    }
}

