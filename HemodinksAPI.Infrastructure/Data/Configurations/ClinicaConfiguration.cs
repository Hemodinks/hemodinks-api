using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class ClinicaConfiguration : IEntityTypeConfiguration<Clinica>
{
    public void Configure(EntityTypeBuilder<Clinica> entity)
    {
        entity.ToTable("Clinicas");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(120);

        entity.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(120);

        entity.Property(e => e.Ativa)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.HasIndex(e => e.Slug)
            .IsUnique();

        entity.HasData(new Clinica
        {
            Id = Clinica.DefaultId,
            Nome = Clinica.DefaultNome,
            Slug = Clinica.DefaultSlug,
            Ativa = true,
            DataCadastro = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
