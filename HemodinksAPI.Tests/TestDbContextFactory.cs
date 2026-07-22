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

    public IAppDbContext AppContext => _appContext;

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

internal sealed class RelationalCbhpmTestAppDbContext : IAppDbContext
{
    private readonly RelationalCbhpmTestDbContext _context;

    public RelationalCbhpmTestAppDbContext(RelationalCbhpmTestDbContext context)
    {
        _context = context;
    }

    public DbSet<CbhpmGeral> CbhpmGeral => _context.CbhpmGeral;

    public DbSet<Clinica> Clinicas => throw new NotSupportedException();
    public DbSet<UsuarioGlobal> UsuariosGlobais => throw new NotSupportedException();
    public DbSet<UsuarioClinica> UsuariosClinicas => throw new NotSupportedException();
    public DbSet<AuditoriaPlataforma> AuditoriasPlataforma => throw new NotSupportedException();
    public DbSet<User> Users => throw new NotSupportedException();
    public DbSet<Perfil> Perfis => throw new NotSupportedException();
    public DbSet<Paciente> Pacientes => throw new NotSupportedException();
    public DbSet<FaturamentoMedico> FaturamentosMedicos => throw new NotSupportedException();
    public DbSet<AtendimentoCirurgico> AtendimentosCirurgicos => throw new NotSupportedException();
    public DbSet<AtendimentoProcedimento> AtendimentoProcedimentos => throw new NotSupportedException();
    public DbSet<Faturamento> Faturamentos => throw new NotSupportedException();
    public DbSet<FaturamentoItem> FaturamentoItens => throw new NotSupportedException();
    public DbSet<Glosa> Glosas => throw new NotSupportedException();
    public DbSet<RecursoGlosa> RecursosGlosa => throw new NotSupportedException();
    public DbSet<ContaReceber> ContasReceber => throw new NotSupportedException();
    public DbSet<Recebimento> Recebimentos => throw new NotSupportedException();
    public DbSet<ConvenioProcedimentoPreco> ConvenioProcedimentoPrecos => throw new NotSupportedException();
    public DbSet<Observacao> Observacoes => throw new NotSupportedException();
    public DbSet<GrupoMedico> GruposMedicos => throw new NotSupportedException();
    public DbSet<GrupoMedicoUsuario> GrupoMedicoUsuarios => throw new NotSupportedException();
    public DbSet<Hospital> Hospitais => throw new NotSupportedException();
    public DbSet<Convenio> Convenios => throw new NotSupportedException();
    public DbSet<Opme> OPME => throw new NotSupportedException();
    public DbSet<PacienteArquivo> PacienteArquivos => throw new NotSupportedException();
    public DbSet<PacienteProcedimento> PacienteProcedimentos => throw new NotSupportedException();
    public DbSet<UserArquivo> UserArquivos => throw new NotSupportedException();
    public DbSet<Licenca> Licencas => throw new NotSupportedException();
    public DbSet<Event> Events => throw new NotSupportedException();
    public DbSet<AgendaNotification> AgendaNotifications => throw new NotSupportedException();
    public DbSet<IdempotencyRequest> IdempotencyRequests => throw new NotSupportedException();
    public DbSet<PasswordResetToken> PasswordResetTokens => throw new NotSupportedException();
    public DbSet<ConfiguracaoSistema> ConfiguracoesSistema => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
