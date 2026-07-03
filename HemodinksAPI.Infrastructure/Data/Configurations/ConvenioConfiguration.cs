using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class ConvenioConfiguration : IEntityTypeConfiguration<Convenio>
{
    public void Configure(EntityTypeBuilder<Convenio> entity)
    {
        entity.ToTable("Convenios");

        entity.HasKey(e => e.IdConvenio);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.DescricaoConvenio)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => new { e.ClinicaId, e.DescricaoConvenio })
            .IsUnique();

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasData(
            new Convenio { IdConvenio = 1, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Amil" },
            new Convenio { IdConvenio = 2, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Bradesco Saúde" },
            new Convenio { IdConvenio = 3, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Cemig Saúde" },
            new Convenio { IdConvenio = 4, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Fusex" },
            new Convenio { IdConvenio = 5, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Geap" },
            new Convenio { IdConvenio = 6, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Ipsemg" },
            new Convenio { IdConvenio = 7, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Particular" },
            new Convenio { IdConvenio = 8, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Sul América" },
            new Convenio { IdConvenio = 9, ClinicaId = Clinica.DefaultId, DescricaoConvenio = "Unimed Uberlândia - Plano  Unimed Intercâmbio" });
    }
}
