using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class UsuarioClinicaConfiguration : IEntityTypeConfiguration<UsuarioClinica>
{
    public void Configure(EntityTypeBuilder<UsuarioClinica> entity)
    {
        entity.ToTable("UsuariosClinicas");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Ativo).IsRequired().HasDefaultValue(true);
        entity.Property(item => item.ClinicaPadrao).IsRequired().HasDefaultValue(false);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(item => new { item.UsuarioGlobalId, item.ClinicaId }).IsUnique();
        entity.HasIndex(item => item.UserId).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.PerfilId, item.Ativo });

        entity.HasOne(item => item.UsuarioGlobal)
            .WithMany(item => item.Clinicas)
            .HasForeignKey(item => item.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.Clinica)
            .WithMany()
            .HasForeignKey(item => item.ClinicaId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.User)
            .WithOne()
            .HasForeignKey<UsuarioClinica>(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.Perfil)
            .WithMany()
            .HasForeignKey(item => item.PerfilId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
