using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class UsuarioGlobalConfiguration : IEntityTypeConfiguration<UsuarioGlobal>
{
    public void Configure(EntityTypeBuilder<UsuarioGlobal> entity)
    {
        entity.ToTable("UsuariosGlobais");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.SecurityVersion).IsConcurrencyToken();
        entity.Property(item => item.Nome).IsRequired().HasMaxLength(255);
        entity.Property(item => item.Email).IsRequired().HasMaxLength(255);
        entity.Property(item => item.Senha).IsRequired().HasMaxLength(500).IsConcurrencyToken();
        entity.Property(item => item.Ativo).IsRequired().HasDefaultValue(true);
        entity.Property(item => item.DataCadastro).IsRequired().HasDefaultValueSql("GETUTCDATE()");
        entity.Property(item => item.TentativasLoginFalhas).IsRequired().HasDefaultValue(0);
        // #150 MFA: default false no banco para escritores antigos; segredos cifrados (payload do Data Protection).
        entity.Property(item => item.MfaHabilitado).IsRequired().HasDefaultValue(false);
        entity.Property(item => item.MfaSegredoProtegido).HasMaxLength(2000);
        entity.Property(item => item.MfaSegredoPendenteProtegido).HasMaxLength(2000);
        // Anti-replay sem corrida: dois logins simultaneos com o mesmo codigo nao passam ambos.
        entity.Property(item => item.MfaUltimoPassoUsado).IsConcurrencyToken();
        entity.HasIndex(item => item.Email).IsUnique();
        entity.HasIndex(item => item.Ativo);
        entity.HasIndex(item => item.BloqueadoAte);
    }
}
