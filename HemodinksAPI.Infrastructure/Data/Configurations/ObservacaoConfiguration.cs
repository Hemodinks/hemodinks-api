using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class ObservacaoConfiguration : IEntityTypeConfiguration<Observacao>
{
    public void Configure(EntityTypeBuilder<Observacao> entity)
    {
        entity.ToTable("Observacoes");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.Texto)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.Medico)
            .HasMaxLength(255);

        entity.Property(e => e.MedicoAuxiliar1)
            .HasMaxLength(255);

        entity.Property(e => e.MedicoAuxiliar2)
            .HasMaxLength(255);

        entity.HasIndex(e => new { e.ClinicaId, e.PacienteId, e.DataCadastro });
        entity.HasIndex(e => new { e.ClinicaId, e.DestinatarioUserId, e.DataLeitura, e.DataCadastro });
        entity.HasIndex(e => new { e.ClinicaId, e.AutorUserId, e.DataCadastro });

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Paciente)
            .WithMany(e => e.Observacoes)
            .HasForeignKey(e => e.PacienteId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ObservacaoPai)
            .WithMany()
            .HasForeignKey(e => e.ObservacaoPaiId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
