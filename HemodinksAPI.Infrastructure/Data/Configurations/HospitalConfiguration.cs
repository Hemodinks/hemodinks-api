using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class HospitalConfiguration : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> entity)
    {
        entity.ToTable("Hospitais");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => e.Nome)
            .IsUnique();

        entity.HasData(
            new Hospital { Id = 1, Nome = "Santa Clara - Mater Dei" },
            new Hospital { Id = 2, Nome = "Santa Genoveva - Mater Dei" },
            new Hospital { Id = 3, Nome = "UMC - Complexo Hospitalar" });
    }
}
