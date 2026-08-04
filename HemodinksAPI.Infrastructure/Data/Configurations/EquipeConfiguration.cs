using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class EquipeConfiguration : IEntityTypeConfiguration<Equipe>
{
    public void Configure(EntityTypeBuilder<Equipe> entity)
    {
        entity.ToTable("Equipes");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Nome).IsRequired().HasMaxLength(120);
        entity.Property(item => item.ModoIdentificacao).IsRequired().HasMaxLength(20);
        entity.Property(item => item.Ativa).IsRequired().HasDefaultValue(true);
        entity.Property(item => item.VersaoSessao).IsRequired().HasDefaultValue(1);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(item => new { item.ClinicaId, item.Nome }).IsUnique();
        entity.HasIndex(item => item.UsuarioLoginId).IsUnique();
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.UsuarioLogin).WithOne().HasForeignKey<Equipe>(item => item.UsuarioLoginId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipeMembroConfiguration : IEntityTypeConfiguration<EquipeMembro>
{
    public void Configure(EntityTypeBuilder<EquipeMembro> entity)
    {
        entity.ToTable("EquipeMembros");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Ativo).IsRequired().HasDefaultValue(true);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(item => new { item.EquipeId, item.UserId }).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.UserId, item.Ativo });
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Equipe).WithMany(item => item.Membros).HasForeignKey(item => item.EquipeId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipeOperadorConfiguration : IEntityTypeConfiguration<EquipeOperador>
{
    public void Configure(EntityTypeBuilder<EquipeOperador> entity)
    {
        entity.ToTable("EquipeOperadores");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.PinHash).HasMaxLength(500);
        entity.Property(item => item.PrecisaTrocarPin).IsRequired().HasDefaultValue(false);
        entity.Property(item => item.TentativasFalhas).IsRequired().HasDefaultValue(0);
        entity.Property(item => item.VersaoSessao).IsRequired().HasDefaultValue(1);
        entity.Property(item => item.Ativo).IsRequired().HasDefaultValue(true);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(item => new { item.EquipeId, item.UserId }).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.Ativo });
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Equipe).WithMany(item => item.Operadores).HasForeignKey(item => item.EquipeId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipeLoginDesafioConfiguration : IEntityTypeConfiguration<EquipeLoginDesafio>
{
    public void Configure(EntityTypeBuilder<EquipeLoginDesafio> entity)
    {
        entity.ToTable("EquipeLoginDesafios");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.TokenHash).IsRequired().HasMaxLength(64);
        entity.Property(item => item.RequestIp).HasMaxLength(45);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(item => new { item.ClinicaId, item.TokenHash }).IsUnique();
        entity.HasIndex(item => new { item.ClinicaId, item.EquipeId, item.ExpiraEm, item.UtilizadoEm });
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Equipe).WithMany().HasForeignKey(item => item.EquipeId).OnDelete(DeleteBehavior.Cascade);
    }
}
