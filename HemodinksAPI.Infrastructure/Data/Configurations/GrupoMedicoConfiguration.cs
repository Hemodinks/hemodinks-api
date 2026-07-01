using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class GrupoMedicoConfiguration : IEntityTypeConfiguration<GrupoMedico>
{
    public void Configure(EntityTypeBuilder<GrupoMedico> entity)
    {
        entity.ToTable("GruposMedicos");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Ativo)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.HasIndex(e => e.Nome)
            .IsUnique();
    }
}
