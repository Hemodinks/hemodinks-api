using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AuthenticationSessionRefreshHistoryConfiguration : IEntityTypeConfiguration<AuthenticationSessionRefreshHistory>
{
    public void Configure(EntityTypeBuilder<AuthenticationSessionRefreshHistory> entity)
    {
        entity.ToTable("AuthenticationSessionRefreshHistory");
        entity.HasKey(item => item.Id);

        entity.Property(item => item.Hash)
            .IsRequired()
            .HasMaxLength(64);
        entity.Property(item => item.RotatedAt).IsRequired();

        // Nao unico de proposito: duas abas renovando com o mesmo token disputam a rowversion da sessao.
        // Um indice unico podia falhar primeiro no INSERT (2601) e virar 500; assim a perdedora recebe o
        // conflito normal e a transacao do SaveChanges desfaz o INSERT junto. Cada hash so sai de uso uma vez.
        entity.HasIndex(item => item.Hash);
        entity.HasIndex(item => item.RotatedAt);
        entity.HasIndex(item => item.SessionId);

        entity.HasOne(item => item.Session)
            .WithMany()
            .HasForeignKey(item => item.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
