using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class PerfilConfiguration : IEntityTypeConfiguration<Perfil>
{
    public void Configure(EntityTypeBuilder<Perfil> entity)
    {
        entity.ToTable("Perfis");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(100);

        entity.HasIndex(e => e.Nome)
            .IsUnique();

        entity.HasData(
            new Perfil { Id = Perfil.AdministradorId, Nome = "Administrador" },
            new Perfil { Id = Perfil.MedicosId, Nome = "Médicos" },
            new Perfil { Id = Perfil.PacientesId, Nome = "Pacientes" },
            new Perfil { Id = Perfil.ControllerId, Nome = "Controller" },
            new Perfil { Id = Perfil.SuperAdministradorId, Nome = "SuperAdministrador" });
    }
}
