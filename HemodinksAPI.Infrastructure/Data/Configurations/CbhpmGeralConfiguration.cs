using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class CbhpmGeralConfiguration : IEntityTypeConfiguration<CbhpmGeral>
{
    public void Configure(EntityTypeBuilder<CbhpmGeral> entity)
    {
        entity.ToTable("CBHPMGeral");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Codigo)
            .IsRequired()
            .HasMaxLength(20);

        entity.Property(e => e.Procedimento)
            .IsRequired()
            .HasMaxLength(1000);

        entity.Property(e => e.Porte)
            .HasMaxLength(10);

        entity.Property(e => e.CustoOperacional)
            .HasColumnType("decimal(18,3)");

        entity.Property(e => e.ValorReferencia)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.Capitulo)
            .HasMaxLength(255);

        entity.Property(e => e.Grupo)
            .HasMaxLength(255);

        entity.HasIndex(e => e.Codigo)
            .IsUnique();

        entity.HasIndex(e => e.Porte);
    }
}
