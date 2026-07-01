using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class PacienteProcedimentoConfiguration : IEntityTypeConfiguration<PacienteProcedimento>
{
    public void Configure(EntityTypeBuilder<PacienteProcedimento> entity)
    {
        entity.ToTable("PacienteProcedimentos");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.CbhpmCodigo)
            .HasMaxLength(20);

        entity.Property(e => e.CbhpmPorte)
            .HasMaxLength(10);

        entity.Property(e => e.Procedimento)
            .IsRequired()
            .HasMaxLength(1000);

        entity.Property(e => e.ValorReferencia)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.Ordem)
            .IsRequired()
            .HasDefaultValue(1);

        entity.HasIndex(e => e.PacienteId);
        entity.HasIndex(e => e.CbhpmCodigo);
    }
}
