using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AgendaNotificationConfiguration : IEntityTypeConfiguration<AgendaNotification>
{
    public void Configure(EntityTypeBuilder<AgendaNotification> entity)
    {
        entity.ToTable("AgendaNotifications");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Message)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.ReadAt);

        entity.HasIndex(e => new { e.RecipientUserId, e.ReadAt, e.CreatedAt });
        entity.HasIndex(e => e.EventId);
        entity.HasIndex(e => e.SenderUserId);

        entity.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.SenderUser)
            .WithMany()
            .HasForeignKey(e => e.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.RecipientUser)
            .WithMany()
            .HasForeignKey(e => e.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
