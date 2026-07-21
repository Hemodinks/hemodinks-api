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

        entity.Property(e => e.FotoClinica)
            .HasColumnType("nvarchar(max)");

        entity.Property(e => e.Ativa)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.Property(e => e.Plano)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Trial");

        entity.Property(e => e.ModulosLiberados)
            .HasMaxLength(500);

        entity.Property(e => e.AssinaturaStatus)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("Trial");

        entity.Property(e => e.TrialAte);
        entity.Property(e => e.AssinaturaValidaAte);
        entity.Property(e => e.LimiteUsuarios);

        entity.HasIndex(e => e.Slug)
            .IsUnique();

        entity.HasData(new Clinica
        {
            Id = Clinica.DefaultId,
            Nome = Clinica.DefaultNome,
            Slug = Clinica.DefaultSlug,
            Ativa = true,
            Plano = "Trial",
            AssinaturaStatus = "Trial",
            DataCadastro = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
