using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class FaturamentoHistoricoArquivoConfiguration : IEntityTypeConfiguration<FaturamentoHistoricoArquivo>
{
    public void Configure(EntityTypeBuilder<FaturamentoHistoricoArquivo> entity)
    {
        entity.ToTable("FaturamentoHistoricoArquivos");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.ClinicaId).IsRequired();
        entity.Property(item => item.Ano).IsRequired();
        entity.Property(item => item.Mes).IsRequired();
        entity.Property(item => item.NomeOriginal).IsRequired().HasMaxLength(255);
        entity.Property(item => item.ContentType).IsRequired().HasMaxLength(120);
        entity.Property(item => item.Url).IsRequired().HasMaxLength(2048);
        entity.Property(item => item.DataUpload).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(item => new { item.ClinicaId, item.Ano, item.Mes, item.DataUpload });
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_FaturamentoHistoricoArquivos_Ano", "[Ano] BETWEEN 1900 AND 2100");
            table.HasCheckConstraint("CK_FaturamentoHistoricoArquivos_Mes", "[Mes] BETWEEN 1 AND 12");
        });
    }
}
