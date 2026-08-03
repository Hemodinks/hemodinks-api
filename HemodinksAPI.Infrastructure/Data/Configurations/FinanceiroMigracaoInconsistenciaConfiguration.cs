using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class FinanceiroMigracaoInconsistenciaConfiguration : IEntityTypeConfiguration<FinanceiroMigracaoInconsistencia>
{
    public void Configure(EntityTypeBuilder<FinanceiroMigracaoInconsistencia> entity)
    {
        entity.ToTable("FinanceiroMigracaoInconsistencias");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Campo).IsRequired().HasMaxLength(100);
        entity.Property(x => x.ValorOriginal).IsRequired().HasMaxLength(2000);
        entity.Property(x => x.Motivo).IsRequired().HasMaxLength(1000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.PacienteId, x.Campo, x.Resolvida });
        entity.HasIndex(x => new { x.ClinicaId, x.Resolvida, x.DataCadastro });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
    }
}
