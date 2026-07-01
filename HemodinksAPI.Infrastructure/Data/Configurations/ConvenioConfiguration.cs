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

        entity.Property(e => e.DescricaoConvenio)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => e.DescricaoConvenio)
            .IsUnique();

        entity.HasData(
            new Convenio { IdConvenio = 1, DescricaoConvenio = "Amil" },
            new Convenio { IdConvenio = 2, DescricaoConvenio = "Bradesco Saúde" },
            new Convenio { IdConvenio = 3, DescricaoConvenio = "Cemig Saúde" },
            new Convenio { IdConvenio = 4, DescricaoConvenio = "Fusex" },
            new Convenio { IdConvenio = 5, DescricaoConvenio = "Geap" },
            new Convenio { IdConvenio = 6, DescricaoConvenio = "Ipsemg" },
            new Convenio { IdConvenio = 7, DescricaoConvenio = "Particular" },
            new Convenio { IdConvenio = 8, DescricaoConvenio = "Sul América" },
            new Convenio { IdConvenio = 9, DescricaoConvenio = "Unimed Uberlândia - Plano  Unimed Intercâmbio" });
    }
}
