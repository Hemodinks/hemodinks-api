using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

// Real relational transactions and production credential/concurrency mappings, without SQL Server services.
internal sealed class PasswordResetSecurityTestDatabase : IAsyncDisposable
{
    public const string Token = "recovery-token-for-relational-tests";
    public const string OldPassword = "OriginalPassword@123";
    public const string NewPassword = "RecoveredPassword@456";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<PasswordResetSecurityDbContext> _options = null!;

    public PasswordResetSecurityDbContext Open() => new(_options);

    public static async Task<PasswordResetSecurityTestDatabase> CreateAsync()
    {
        var database = new PasswordResetSecurityTestDatabase();
        await database._connection.OpenAsync();
        database._options = new DbContextOptionsBuilder<PasswordResetSecurityDbContext>()
            .UseSqlite(database._connection).Options;
        await using var db = database.Open();
        await db.Database.EnsureCreatedAsync();
        var hash = new PasswordHasher().HashPassword(OldPassword);
        var user = new User { Nome = "Recovery", Email = "recovery@example.com", Telefone = "11999999999", Senha = hash, PrecisaTrocarSenha = false };
        var global = new UsuarioGlobal { Nome = user.Nome, Email = user.Email, Senha = hash, SecurityVersion = Guid.NewGuid() };
        var membership = new UsuarioClinica { User = user, UsuarioGlobal = global, ClinicaId = 1, PerfilId = user.PerfilId };
        db.UsuariosClinicas.Add(membership);
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            User = user, ClinicaId = 1, TokenHash = PasswordResetRules.HashToken(Token),
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
        db.AuthenticationSessions.Add(new AuthenticationSession
        {
            Id = Guid.NewGuid(), UsuarioClinica = membership, UsuarioGlobal = global,
            SecurityVersion = global.SecurityVersion, RefreshTokenHash = "test-refresh-hash"
        });
        await db.SaveChangesAsync();
        return database;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

internal sealed class PasswordResetSecurityDbContext(DbContextOptions<PasswordResetSecurityDbContext> options)
    : DbContext(options), IPlatformPasswordResetDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UsuarioGlobal> UsuariosGlobais => Set<UsuarioGlobal>();
    public DbSet<UsuarioClinica> UsuariosClinicas => Set<UsuarioClinica>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuthenticationSession> AuthenticationSessions => Set<AuthenticationSession>();
    public Func<Task>? BeforeCredentialSave { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (BeforeCredentialSave != null && ChangeTracker.Entries<PasswordResetToken>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.UsedAt != null))
            await BeforeCredentialSave();
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>().Ignore(x => x.Clinica).Ignore(x => x.Perfil).Ignore(x => x.Paciente)
            .Ignore(x => x.Licenca).Ignore(x => x.Events).Ignore(x => x.MedicalEvents)
            .Ignore(x => x.Arquivos).Ignore(x => x.GruposMedicos)
            .Ignore(x => x.ObservacoesEnviadas).Ignore(x => x.ObservacoesRecebidas);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly, type => type.Name is
            "UsuarioGlobalConfiguration" or "UsuarioClinicaConfiguration" or "PasswordResetTokenConfiguration" or "AuthenticationSessionConfiguration");
        builder.Ignore<Clinica>();
        builder.Ignore<Perfil>();
        // SQLite has no SQL Server-generated rowversion. Keep its concurrency mapping, supply the value.
        builder.Entity<AuthenticationSession>().Property(x => x.RowVersion).ValueGeneratedNever();
        foreach (var entity in builder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.GetDefaultValueSql() == "GETUTCDATE()") property.SetDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
