using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AuditoriaClinicaConfiguration : IEntityTypeConfiguration<AuditoriaClinica>
{
    public void Configure(EntityTypeBuilder<AuditoriaClinica> entity)
    {
        entity.ToTable("AuditoriasClinica");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.ClinicaId).IsRequired();
        entity.Property(item => item.Acao).IsRequired().HasMaxLength(60);
        entity.Property(item => item.Recurso).IsRequired().HasMaxLength(60);
        entity.Property(item => item.RecursoId).HasMaxLength(40);
        entity.Property(item => item.IpTruncado).HasMaxLength(64);
        entity.Property(item => item.DataUtc).IsRequired();

        entity.HasIndex(item => new { item.ClinicaId, item.DataUtc });
        entity.HasIndex(item => new { item.ClinicaId, item.Recurso, item.RecursoId, item.DataUtc });
        entity.HasIndex(item => item.DataUtc);

        entity.HasOne(item => item.Clinica)
            .WithMany()
            .HasForeignKey(item => item.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
