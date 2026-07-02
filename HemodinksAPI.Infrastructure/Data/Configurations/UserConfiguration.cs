using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Nome)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(e => e.Telefone)
            .IsRequired()
            .HasMaxLength(20);

        entity.Property(e => e.Cpf)
            .HasMaxLength(11);

        entity.Property(e => e.Crm)
            .HasMaxLength(20);

        entity.Property(e => e.CrmUf)
            .HasMaxLength(2);

        entity.Property(e => e.FotoPerfil)
            .HasColumnType("nvarchar(max)");

        entity.Property(e => e.Senha)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(e => e.DataCadastro)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(e => e.DataAtualizacao);

        entity.Property(e => e.DataNascimento);

        entity.Property(e => e.Ativo)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.PrecisaTrocarSenha)
            .IsRequired()
            .HasDefaultValue(true);

        entity.Property(e => e.PerfilId)
            .IsRequired()
            .HasDefaultValue(Perfil.MedicosId);

        entity.HasIndex(e => e.Email)
            .IsUnique();

        entity.HasIndex(e => e.Telefone);

        entity.HasIndex(e => e.Cpf)
            .IsUnique()
            .HasFilter("[Cpf] IS NOT NULL");

        entity.HasIndex(e => e.PerfilId);

        entity.HasOne(e => e.Perfil)
            .WithMany(e => e.Users)
            .HasForeignKey(e => e.PerfilId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(e => e.Arquivos)
            .WithOne(e => e.User)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Licenca)
            .WithOne(e => e.User)
            .HasForeignKey<Licenca>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.GruposMedicos)
            .WithOne(e => e.User)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.ObservacoesEnviadas)
            .WithOne(e => e.AutorUser)
            .HasForeignKey(e => e.AutorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(e => e.ObservacoesRecebidas)
            .WithOne(e => e.DestinatarioUser)
            .HasForeignKey(e => e.DestinatarioUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
