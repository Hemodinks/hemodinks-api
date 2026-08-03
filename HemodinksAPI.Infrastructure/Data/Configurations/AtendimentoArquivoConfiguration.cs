using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AtendimentoArquivoConfiguration : IEntityTypeConfiguration<AtendimentoArquivo>
{
    public void Configure(EntityTypeBuilder<AtendimentoArquivo> entity)
    {
        entity.ToTable("AtendimentoArquivos");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.ClinicaId).IsRequired();
        entity.Property(e => e.NomeOriginal).IsRequired().HasMaxLength(255);
        entity.Property(e => e.ContentType).IsRequired().HasMaxLength(120);
        entity.Property(e => e.Url).IsRequired().HasMaxLength(2048);
        entity.Property(e => e.DataUpload).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(e => new { e.ClinicaId, e.AtendimentoCirurgicoId });
        entity.HasOne(e => e.Clinica).WithMany().HasForeignKey(e => e.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(e => e.AtendimentoCirurgico).WithMany(e => e.Arquivos)
            .HasForeignKey(e => e.AtendimentoCirurgicoId).OnDelete(DeleteBehavior.Cascade);
    }
}
