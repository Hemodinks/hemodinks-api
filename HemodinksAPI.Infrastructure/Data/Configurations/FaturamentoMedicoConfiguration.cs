using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class FaturamentoMedicoConfiguration : IEntityTypeConfiguration<FaturamentoMedico>
{
    public void Configure(EntityTypeBuilder<FaturamentoMedico> entity)
    {
        entity.ToTable("FaturamentosMedicos");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.ClinicaId)
            .IsRequired();

        entity.Property(e => e.HonorariosCirurgiao)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.HonorariosAuxiliares)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.HonorariosAnestesista)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.AnestesistaFaturadoSeparado)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.Anestesista)
            .HasMaxLength(255);

        entity.Property(e => e.CodigoTussCbhpmAmb)
            .HasMaxLength(1000);

        entity.Property(e => e.PorteCirurgicoAnestesico)
            .HasMaxLength(255);

        entity.Property(e => e.GuiaAutorizacaoConvenio)
            .HasMaxLength(255);

        entity.Property(e => e.GuiaInternacaoOuSadt)
            .HasMaxLength(255);

        entity.Property(e => e.OpmeMateriaisEspeciais)
            .HasMaxLength(255);

        entity.Property(e => e.TissXmlStatus)
            .HasMaxLength(255);

        entity.Property(e => e.ValorGlosa)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.GlosaStatus)
            .HasMaxLength(255);

        entity.Property(e => e.RecursoGlosa)
            .HasMaxLength(1000);

        entity.Property(e => e.ConferenciaPagamentoRealizada)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.RepasseMedico)
            .HasColumnType("decimal(18,2)");

        entity.Property(e => e.RepasseMedicoObservacao)
            .HasMaxLength(1000);

        entity.Property(e => e.TipoFaturamentoParticular)
            .HasMaxLength(100);

        entity.Property(e => e.ReciboNotaContrato)
            .HasMaxLength(255);

        entity.Property(e => e.Observacoes)
            .HasMaxLength(2000);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.HasIndex(e => new { e.ClinicaId, e.PacienteId })
            .IsUnique();

        entity.HasIndex(e => new { e.ClinicaId, e.ConferenciaPagamentoRealizada });

        entity.HasIndex(e => new { e.ClinicaId, e.DataCadastro });

        entity.HasOne(e => e.Clinica)
            .WithMany()
            .HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
