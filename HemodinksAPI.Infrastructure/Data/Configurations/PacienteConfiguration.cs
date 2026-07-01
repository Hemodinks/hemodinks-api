using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class PacienteConfiguration : IEntityTypeConfiguration<Paciente>
{
    public void Configure(EntityTypeBuilder<Paciente> entity)
    {
        entity.ToTable("Pacientes");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.NomePaciente)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Diagnostico)
            .HasMaxLength(100);

        entity.Property(e => e.TratamentoMedico)
            .HasMaxLength(100);

        entity.Property(e => e.Hospital)
            .HasMaxLength(255);

        entity.Property(e => e.Medico)
            .HasMaxLength(255);

        entity.HasIndex(e => e.MedicoUserId);

        entity.Property(e => e.MedicoAuxiliar1)
            .HasMaxLength(255);

        entity.HasIndex(e => e.MedicoAuxiliar1UserId);

        entity.Property(e => e.MedicoAuxiliar2)
            .HasMaxLength(255);

        entity.HasIndex(e => e.MedicoAuxiliar2UserId);

        entity.Property(e => e.Convenio)
            .HasMaxLength(255);

        entity.HasIndex(e => e.ConvenioId);

        entity.Property(e => e.OpmeFornecedor)
            .HasMaxLength(255);

        entity.HasIndex(e => e.OpmeFornecedorId);

        entity.Property(e => e.CbhpmCodigo)
            .HasMaxLength(20);

        entity.Property(e => e.CbhpmPorte)
            .HasMaxLength(10);

        entity.Property(e => e.Procedimento)
            .HasMaxLength(1000);

        entity.Property(e => e.Autorizacao)
            .HasMaxLength(255);

        entity.Property(e => e.Pagamento)
            .HasMaxLength(255);

        entity.Property(e => e.RepasseGlosa)
            .HasMaxLength(255);

        entity.Property(e => e.StatusPago)
            .IsRequired()
            .HasDefaultValue(false);

        entity.HasIndex(e => e.UserId)
            .IsUnique();

        entity.HasIndex(e => e.CbhpmCodigo);
        entity.HasIndex(e => e.HospitalId);

        entity.HasOne(e => e.User)
            .WithOne(e => e.Paciente)
            .HasForeignKey<Paciente>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.HospitalReferencia)
            .WithMany(e => e.Pacientes)
            .HasForeignKey(e => e.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.MedicoUser)
            .WithMany()
            .HasForeignKey(e => e.MedicoUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.MedicoAuxiliar1User)
            .WithMany()
            .HasForeignKey(e => e.MedicoAuxiliar1UserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.MedicoAuxiliar2User)
            .WithMany()
            .HasForeignKey(e => e.MedicoAuxiliar2UserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.ConvenioReferencia)
            .WithMany(e => e.Pacientes)
            .HasForeignKey(e => e.ConvenioId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.OpmeFornecedorReferencia)
            .WithMany(e => e.Pacientes)
            .HasForeignKey(e => e.OpmeFornecedorId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(e => e.Procedimentos)
            .WithOne(e => e.Paciente)
            .HasForeignKey(e => e.PacienteId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.FaturamentoMedico)
            .WithOne(e => e.Paciente)
            .HasForeignKey<FaturamentoMedico>(e => e.PacienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
