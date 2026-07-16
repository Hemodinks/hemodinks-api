using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class ConfiguracaoSistemaConfiguration : IEntityTypeConfiguration<ConfiguracaoSistema>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoSistema> entity)
    {
        entity.ToTable("ConfiguracoesSistema");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.NomeEmpresa)
            .IsRequired()
            .HasMaxLength(120);

        entity.Property(e => e.FotoEmpresa)
            .HasColumnType("nvarchar(max)");

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.HasIndex(e => e.ClinicaId)
            .IsUnique();

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasData(new ConfiguracaoSistema
        {
            Id = ConfiguracaoSistema.DefaultId,
            ClinicaId = Clinica.DefaultId,
            NomeEmpresa = ConfiguracaoSistema.DefaultNomeEmpresa,
            DataCadastro = new DateTime(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
