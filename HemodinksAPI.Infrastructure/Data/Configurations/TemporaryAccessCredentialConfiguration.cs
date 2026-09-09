using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class TemporaryAccessCredentialConfiguration : IEntityTypeConfiguration<TemporaryAccessCredential>
{
    public void Configure(EntityTypeBuilder<TemporaryAccessCredential> entity)
    {
        entity.ToTable("TemporaryAccessCredentials");
        entity.HasKey(x => x.UsuarioGlobalId);
        entity.HasOne(x => x.UsuarioGlobal).WithOne().HasForeignKey<TemporaryAccessCredential>(x => x.UsuarioGlobalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        entity.Property(x => x.Id).IsConcurrencyToken();
        entity.Property(x => x.UsedAtUtc).IsConcurrencyToken();
        entity.Property(x => x.RevokedAtUtc).IsConcurrencyToken();
        entity.HasIndex(x => new { x.ClinicaId, x.UserId });
    }
}
