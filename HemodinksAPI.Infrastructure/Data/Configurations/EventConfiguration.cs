using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> entity)
    {
        entity.ToTable("Events");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Description)
            .HasMaxLength(2000);

        entity.Property(e => e.Start)
            .IsRequired();

        entity.Property(e => e.End)
            .IsRequired();

        entity.Property(e => e.NotifyMedicalProfile)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.NotifyUser)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.IsCompleted)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.NextReminderAt);

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(e => new { e.ClinicaId, e.UserId });
        entity.HasIndex(e => new { e.ClinicaId, e.MedicalUserId });
        entity.HasIndex(e => new { e.ClinicaId, e.Start, e.End, e.IsCompleted });
        entity.HasIndex(e => new { e.ClinicaId, e.NextReminderAt, e.IsCompleted });

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.User)
            .WithMany(e => e.Events)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.MedicalUser)
            .WithMany(e => e.MedicalEvents)
            .HasForeignKey(e => e.MedicalUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
