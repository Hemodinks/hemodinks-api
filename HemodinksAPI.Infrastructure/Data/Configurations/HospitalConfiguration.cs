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

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => new { e.ClinicaId, e.Nome })
            .IsUnique();

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasData(
            new Hospital { Id = 1, ClinicaId = Clinica.DefaultId, Nome = "Santa Clara - Mater Dei" },
            new Hospital { Id = 2, ClinicaId = Clinica.DefaultId, Nome = "Santa Genoveva - Mater Dei" },
            new Hospital { Id = 3, ClinicaId = Clinica.DefaultId, Nome = "UMC - Complexo Hospitalar" });
    }
}
