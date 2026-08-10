using HemodinksAPI.Application.Features.Common;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class SqlServerFullTextSearchCapability(AppDbContext context) : IFullTextSearchCapability
{
    public bool IsSupported => string.Equals(
        context.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);
}
