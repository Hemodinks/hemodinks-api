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

        entity.Property(e => e.IdFornecedor)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.Fornecedor)
            .IsRequired()
            .HasMaxLength(255);

        entity.HasIndex(e => e.Fornecedor)
            .IsUnique();

        entity.HasData(
            new Opme { IdFornecedor = 1, Fornecedor = "Promedom" },
            new Opme { IdFornecedor = 2, Fornecedor = "AVL" },
            new Opme { IdFornecedor = 3, Fornecedor = "GE" },
            new Opme { IdFornecedor = 4, Fornecedor = "Spyner" });
    }
}
