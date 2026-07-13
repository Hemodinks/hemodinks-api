using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto de banco de dados da aplicação
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private readonly IClinicaContext _clinicaContext;

    public DbSet<Clinica> Clinicas { get; set; } = null!;

    /// <summary>
    /// DbSet de usuários
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Perfil> Perfis { get; set; } = null!;

    public DbSet<Paciente> Pacientes { get; set; } = null!;

    public DbSet<FaturamentoMedico> FaturamentosMedicos { get; set; } = null!;

    public DbSet<Observacao> Observacoes { get; set; } = null!;

    public DbSet<GrupoMedico> GruposMedicos { get; set; } = null!;

    public DbSet<GrupoMedicoUsuario> GrupoMedicoUsuarios { get; set; } = null!;

    public DbSet<Hospital> Hospitais { get; set; } = null!;

    public DbSet<Convenio> Convenios { get; set; } = null!;

    public DbSet<Opme> OPME { get; set; } = null!;

    public DbSet<PacienteArquivo> PacienteArquivos { get; set; } = null!;

    public DbSet<PacienteProcedimento> PacienteProcedimentos { get; set; } = null!;

    public DbSet<UserArquivo> UserArquivos { get; set; } = null!;

    public DbSet<CbhpmGeral> CbhpmGeral { get; set; } = null!;

    public DbSet<Licenca> Licencas { get; set; } = null!;

    public DbSet<Event> Events { get; set; } = null!;

    public DbSet<AgendaNotification> AgendaNotifications { get; set; } = null!;

    public DbSet<IdempotencyRequest> IdempotencyRequests { get; set; } = null!;

    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;

    public DbSet<ConfiguracaoSistema> ConfiguracoesSistema { get; set; } = null!;

    private int? CurrentClinicaId => _clinicaContext.ClinicaId;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IClinicaContext clinicaContext) : base(options)
    {
        _clinicaContext = clinicaContext;
    }

    /// <summary>
    /// Configuração do modelo de dados
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyClinicaQueryFilters(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyDefaultClinicaIds();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyDefaultClinicaIds();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyDefaultClinicaIds()
    {
        foreach (var entry in ChangeTracker.Entries<IClinicaOwnedEntity>()
                     .Where(entry => entry.State == EntityState.Added && entry.Entity.ClinicaId <= 0))
        {
            entry.Entity.ClinicaId = _clinicaContext.ClinicaId ?? Clinica.DefaultId;
        }
    }

    private void ApplyClinicaQueryFilters(ModelBuilder modelBuilder)
    {
        ApplyClinicaQueryFilter<User>(modelBuilder);
        ApplyClinicaQueryFilter<Paciente>(modelBuilder);
        ApplyClinicaQueryFilter<FaturamentoMedico>(modelBuilder);
        ApplyClinicaQueryFilter<Observacao>(modelBuilder);
        ApplyClinicaQueryFilter<GrupoMedico>(modelBuilder);
        ApplyClinicaQueryFilter<GrupoMedicoUsuario>(modelBuilder);
        ApplyClinicaQueryFilter<Hospital>(modelBuilder);
        ApplyClinicaQueryFilter<Convenio>(modelBuilder);
        ApplyClinicaQueryFilter<Opme>(modelBuilder);
        ApplyClinicaQueryFilter<PacienteArquivo>(modelBuilder);
        ApplyClinicaQueryFilter<PacienteProcedimento>(modelBuilder);
        ApplyClinicaQueryFilter<UserArquivo>(modelBuilder);
        ApplyClinicaQueryFilter<Licenca>(modelBuilder);
        ApplyClinicaQueryFilter<Event>(modelBuilder);
        ApplyClinicaQueryFilter<AgendaNotification>(modelBuilder);
        ApplyClinicaQueryFilter<PasswordResetToken>(modelBuilder);
        ApplyClinicaQueryFilter<ConfiguracaoSistema>(modelBuilder);
    }

    private void ApplyClinicaQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IClinicaOwnedEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(entity => CurrentClinicaId == null || entity.ClinicaId == CurrentClinicaId);
    }
}
