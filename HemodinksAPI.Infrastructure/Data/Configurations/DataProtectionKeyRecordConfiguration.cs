using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class DataProtectionKeyRecordConfiguration : IEntityTypeConfiguration<DataProtectionKeyRecord>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRecord> entity)
    {
        entity.ToTable("DataProtectionKeys");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.FriendlyName)
            .HasMaxLength(256);

        entity.Property(e => e.Xml)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        entity.Property(e => e.DataCriacao)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");
    }
}
