using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

public sealed class EmailChangeRequestConfiguration : IEntityTypeConfiguration<EmailChangeRequest>
{
    public void Configure(EntityTypeBuilder<EmailChangeRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NewEmail).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UsedAt).IsConcurrencyToken();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
