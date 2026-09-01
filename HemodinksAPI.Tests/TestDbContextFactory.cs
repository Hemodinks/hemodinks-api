using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

internal static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        context.Database.EnsureCreated();

        return context;
    }

    public static RelationalCbhpmTestDbContextLease CreateRelationalCbhpm()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<RelationalCbhpmTestDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new RelationalCbhpmTestDbContext(options);
        context.Database.EnsureCreated();

        return new RelationalCbhpmTestDbContextLease(context, connection);
    }
}

internal sealed class RelationalCbhpmTestDbContextLease : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RelationalCbhpmTestAppDbContext _appContext;

    public RelationalCbhpmTestDbContextLease(RelationalCbhpmTestDbContext context, SqliteConnection connection)
    {
        Context = context;
        _connection = connection;
        _appContext = new RelationalCbhpmTestAppDbContext(context);
    }

    public RelationalCbhpmTestDbContext Context { get; }

    public ICbhpmFeatureDbContext AppContext => _appContext;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Context.DisposeAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}

internal sealed class RelationalCbhpmTestDbContext : DbContext
{
    public RelationalCbhpmTestDbContext(DbContextOptions<RelationalCbhpmTestDbContext> options)
        : base(options)
    {
    }

    public DbSet<CbhpmGeral> CbhpmGeral => Set<CbhpmGeral>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CbhpmGeral>();

        entity.ToTable("CBHPMGeral");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Codigo).IsRequired().HasMaxLength(20);
        entity.Property(item => item.Procedimento).IsRequired().HasMaxLength(1000);
        entity.Property(item => item.Porte).HasMaxLength(10);
        entity.Property(item => item.CustoOperacional).HasColumnType("decimal(18,3)");
        entity.Property(item => item.ValorReferencia).HasColumnType("decimal(18,2)");
        entity.Property(item => item.Capitulo).HasMaxLength(255);
        entity.Property(item => item.Grupo).HasMaxLength(255);
        entity.HasIndex(item => item.Codigo).IsUnique();
        entity.HasIndex(item => item.Porte);
    }
}

internal sealed class RelationalCbhpmTestAppDbContext : ICbhpmFeatureDbContext
{
    private readonly RelationalCbhpmTestDbContext _context;

    public RelationalCbhpmTestAppDbContext(RelationalCbhpmTestDbContext context)
    {
        _context = context;
    }

    public DbSet<CbhpmGeral> CbhpmGeral => _context.CbhpmGeral;

    public DbSet<Hospital> Hospitais => throw new NotSupportedException();
    public DbSet<Convenio> Convenios => throw new NotSupportedException();
    public DbSet<Opme> OPME => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
