using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class LicencaConfiguration : IEntityTypeConfiguration<Licenca>
{
    public void Configure(EntityTypeBuilder<Licenca> entity)
    {
        entity.ToTable("Licencas");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.Plano)
            .IsRequired()
            .HasMaxLength(30);

        entity.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(30);

        entity.Property(e => e.DataInicioTrial)
            .IsRequired();

        entity.Property(e => e.DataFimTrial)
            .IsRequired();

        entity.Property(e => e.FeaturesLiberadas)
            .HasMaxLength(1000);

        entity.Property(e => e.Observacoes)
            .HasMaxLength(1000);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(e => new { e.ClinicaId, e.UserId })
            .IsUnique();

        entity.HasIndex(e => e.ClinicaId);

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
