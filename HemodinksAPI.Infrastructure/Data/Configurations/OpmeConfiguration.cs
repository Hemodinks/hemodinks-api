using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class OpmeConfiguration : IEntityTypeConfiguration<Opme>
{
    public void Configure(EntityTypeBuilder<Opme> entity)
    {
        entity.ToTable("OPME");

        entity.HasKey(e => e.IdFornecedor);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.IdFornecedor)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Fornecedor)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => new { e.ClinicaId, e.Fornecedor })
            .IsUnique();

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasData(
            new Opme { IdFornecedor = 1, ClinicaId = Clinica.DefaultId, Fornecedor = "Promedom" },
            new Opme { IdFornecedor = 2, ClinicaId = Clinica.DefaultId, Fornecedor = "AVL" },
            new Opme { IdFornecedor = 3, ClinicaId = Clinica.DefaultId, Fornecedor = "GE" },
            new Opme { IdFornecedor = 4, ClinicaId = Clinica.DefaultId, Fornecedor = "Spyner" });
    }
}
