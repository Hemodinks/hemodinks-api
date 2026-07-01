using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class GrupoMedicoUsuarioConfiguration : IEntityTypeConfiguration<GrupoMedicoUsuario>
{
    public void Configure(EntityTypeBuilder<GrupoMedicoUsuario> entity)
    {
        entity.ToTable("GrupoMedicoUsuarios");

        entity.HasKey(e => new { e.GrupoMedicoId, e.UserId });

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(e => e.UserId);

        entity.HasOne(e => e.GrupoMedico)
            .WithMany(e => e.Membros)
            .HasForeignKey(e => e.GrupoMedicoId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.User)
            .WithMany(e => e.GruposMedicos)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
