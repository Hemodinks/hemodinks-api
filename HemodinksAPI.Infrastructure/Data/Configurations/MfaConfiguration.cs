using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class MfaCodigoRecuperacaoConfiguration : IEntityTypeConfiguration<MfaCodigoRecuperacao>
{
    public void Configure(EntityTypeBuilder<MfaCodigoRecuperacao> entity)
    {
        entity.ToTable("MfaCodigosRecuperacao");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.CodigoHash).IsRequired().HasMaxLength(64);
        // Uso unico mesmo com duas requisicoes simultaneas.
        entity.Property(item => item.UsadoEm).IsConcurrencyToken();
        entity.Property(item => item.DataCadastro).IsRequired();
        entity.HasIndex(item => new { item.UsuarioGlobalId, item.CodigoHash }).IsUnique();
        entity.HasOne(item => item.UsuarioGlobal).WithMany().HasForeignKey(item => item.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MfaLoginDesafioConfiguration : IEntityTypeConfiguration<MfaLoginDesafio>
{
    public void Configure(EntityTypeBuilder<MfaLoginDesafio> entity)
    {
        entity.ToTable("MfaLoginDesafios");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.TokenHash).IsRequired().HasMaxLength(64);
        // Uso unico e contagem de tentativas sem corrida entre requisicoes simultaneas.
        entity.Property(item => item.UtilizadoEm).IsConcurrencyToken();
        entity.Property(item => item.Tentativas).IsConcurrencyToken();
        entity.Property(item => item.DataCadastro).IsRequired();
        entity.HasIndex(item => new { item.ClinicaId, item.TokenHash }).IsUnique();
        entity.HasIndex(item => item.ExpiraEm);
        entity.HasOne(item => item.Clinica).WithMany().HasForeignKey(item => item.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.UsuarioGlobal).WithMany().HasForeignKey(item => item.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
