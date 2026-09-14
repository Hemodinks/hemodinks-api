using System.Data.Common;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Clinics;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

internal sealed class TestingSqlConnectionFactory(Func<DbConnection> create) : ISqlConnectionFactory
{
    public DbConnection CreateConnection() => create();
}

// The endpoint harness uses EF InMemory; SQL behavior is tested separately against SQL Server.
internal sealed class TestingPublicClinicDirectory(AppDbContext context) : IPublicClinicDirectory
{
    public Task<List<PublicClinicSummary>> ListActiveAsync(string? search, CancellationToken cancellationToken) =>
        context.Clinicas.AsNoTracking()
            .Where(c => c.Ativa && (string.IsNullOrEmpty(search) || c.Nome.Contains(search) || c.Slug.Contains(search)))
            .OrderBy(c => c.Nome).ThenBy(c => c.Id).Take(50)
            .Select(c => new PublicClinicSummary(c.Id, c.Nome, c.Slug, c.FotoClinica != null && c.FotoClinica != ""))
            .ToListAsync(cancellationToken);

    public Task<string?> FindActivePhotoReferenceAsync(string slug, CancellationToken cancellationToken) =>
        context.Clinicas.Where(c => c.Ativa && c.Slug == slug)
            .Select(c => c.FotoClinica).FirstOrDefaultAsync(cancellationToken);
}

internal sealed class TestingDatabaseReadinessProbe(AppDbContext context) : IDatabaseReadinessProbe
{
    public async Task<DatabaseReadinessResult> CheckAsync(bool validateSchema, CancellationToken cancellationToken) =>
        new(await context.Database.CanConnectAsync(cancellationToken), []);
}
