using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AuditoriaPlataformaConfiguration : IEntityTypeConfiguration<AuditoriaPlataforma>
{
    public void Configure(EntityTypeBuilder<AuditoriaPlataforma> entity)
    {
        entity.ToTable("AuditoriasPlataforma");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Acao).IsRequired().HasMaxLength(100);
        entity.Property(item => item.Recurso).IsRequired().HasMaxLength(100);
        entity.Property(item => item.EntidadeId).HasMaxLength(100);
        entity.Property(item => item.DetalhesJson).HasColumnType("nvarchar(max)");
        entity.Property(item => item.Ip).HasMaxLength(64);
        entity.Property(item => item.UserAgent).HasMaxLength(500);
        entity.Property(item => item.RequestId).HasMaxLength(100);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(item => item.DataCadastro);
        entity.HasIndex(item => new { item.UsuarioGlobalId, item.DataCadastro });
        entity.HasIndex(item => new { item.ClinicaId, item.DataCadastro });
        entity.HasIndex(item => new { item.Acao, item.DataCadastro });

        entity.HasOne(item => item.UsuarioGlobal)
            .WithMany()
            .HasForeignKey(item => item.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Clinica)
            .WithMany()
            .HasForeignKey(item => item.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
