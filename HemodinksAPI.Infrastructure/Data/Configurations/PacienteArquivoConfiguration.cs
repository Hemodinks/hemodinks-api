using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class PacienteArquivoConfiguration : IEntityTypeConfiguration<PacienteArquivo>
{
    public void Configure(EntityTypeBuilder<PacienteArquivo> entity)
    {
        entity.ToTable("PacienteArquivos");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.NomeOriginal)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(120);

        entity.Property(e => e.Url)
            .IsRequired()
            .HasMaxLength(2048);

        entity.Property(e => e.DataUpload)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(e => e.PacienteId);

        entity.HasOne(e => e.Paciente)
            .WithMany(e => e.Arquivos)
            .HasForeignKey(e => e.PacienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
