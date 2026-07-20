using System.Reflection;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto de banco de dados da aplicação
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private static readonly MethodInfo ApplyClinicaQueryFilterMethod = typeof(AppDbContext)
        .GetMethod(nameof(ApplyClinicaQueryFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo PrincipalExistsInClinicaMethod = typeof(AppDbContext)
        .GetMethod(nameof(PrincipalExistsInClinica), BindingFlags.Instance | BindingFlags.NonPublic)!;

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

    private bool IsPlatformScope => _clinicaContext.IsPlatformScope;

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
        ValidateTenantChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateTenantChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ValidateTenantChanges()
    {
        var entries = ChangeTracker.Entries<IClinicaOwnedEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        if (!IsPlatformScope && !CurrentClinicaId.HasValue)
        {
            throw new InvalidOperationException("Operacao tenant-scoped sem clinica resolvida.");
        }

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added && entry.Entity.ClinicaId <= 0)
            {
                entry.Entity.ClinicaId = CurrentClinicaId
                    ?? throw new InvalidOperationException("ClinicaId deve ser informado em operacoes de plataforma.");
            }

            if (!IsPlatformScope && entry.Entity.ClinicaId != CurrentClinicaId)
            {
                throw new InvalidOperationException(
                    $"ClinicaId divergente em {entry.Metadata.ClrType.Name}. Esperado {CurrentClinicaId}, recebido {entry.Entity.ClinicaId}.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(nameof(IClinicaOwnedEntity.ClinicaId)).OriginalValue is int originalClinicaId
                && originalClinicaId != entry.Entity.ClinicaId)
            {
                throw new InvalidOperationException("Nao e permitido transferir registros entre clinicas.");
            }

            ValidateTenantForeignKeys(entry);
        }
    }

    private void ValidateTenantForeignKeys(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IClinicaOwnedEntity> entry)
    {
        foreach (var foreignKey in entry.Metadata.GetForeignKeys()
                     .Where(item => typeof(IClinicaOwnedEntity).IsAssignableFrom(item.PrincipalEntityType.ClrType)))
        {
            var values = foreignKey.Properties
                .Select(property => entry.Property(property.Name).CurrentValue)
                .ToArray();

            if (values.Any(value => value == null) || values.All(IsDefaultKeyValue))
            {
                continue;
            }

            var principalKey = foreignKey.PrincipalKey.Properties;
            var trackedPrincipal = ChangeTracker.Entries()
                .FirstOrDefault(candidate => candidate.Metadata.ClrType == foreignKey.PrincipalEntityType.ClrType
                    && principalKey.Select((property, index) => Equals(candidate.Property(property.Name).CurrentValue, values[index])).All(matches => matches));

            if (trackedPrincipal?.Entity is IClinicaOwnedEntity trackedTenantEntity)
            {
                if (trackedTenantEntity.ClinicaId != entry.Entity.ClinicaId)
                {
                    ThrowCrossTenantRelationship(entry, foreignKey);
                }

                continue;
            }

            if (values.Length != 1 || values[0] is not int principalId)
            {
                throw new InvalidOperationException("Relacionamento tenant-scoped com chave nao suportada.");
            }

            var existsInSameClinica = (bool)PrincipalExistsInClinicaMethod
                .MakeGenericMethod(foreignKey.PrincipalEntityType.ClrType)
                .Invoke(this, [principalKey[0].Name, principalId, entry.Entity.ClinicaId])!;

            if (!existsInSameClinica)
            {
                ThrowCrossTenantRelationship(entry, foreignKey);
            }
        }
    }

    private bool PrincipalExistsInClinica<TEntity>(string keyName, int keyValue, int clinicaId)
        where TEntity : class, IClinicaOwnedEntity
    {
        return Set<TEntity>()
            .IgnoreQueryFilters()
            .Any(entity => EF.Property<int>(entity, keyName) == keyValue && entity.ClinicaId == clinicaId);
    }

    private static bool IsDefaultKeyValue(object? value)
    {
        return value switch
        {
            int intValue => intValue == 0,
            long longValue => longValue == 0,
            Guid guidValue => guidValue == Guid.Empty,
            _ => false
        };
    }

    private static void ThrowCrossTenantRelationship(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IClinicaOwnedEntity> entry,
        Microsoft.EntityFrameworkCore.Metadata.IForeignKey foreignKey)
    {
        throw new InvalidOperationException(
            $"Relacionamento entre clinicas diferentes: {entry.Metadata.ClrType.Name} -> {foreignKey.PrincipalEntityType.ClrType.Name}.");
    }

    private void ApplyClinicaQueryFilters(ModelBuilder modelBuilder)
    {
        var clinicaOwnedEntityTypes = modelBuilder.Model
            .GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(entityClrType => typeof(IClinicaOwnedEntity).IsAssignableFrom(entityClrType))
            .Distinct();

        foreach (var entityClrType in clinicaOwnedEntityTypes)
        {
            ApplyClinicaQueryFilterMethod
                .MakeGenericMethod(entityClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyClinicaQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IClinicaOwnedEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(entity => IsPlatformScope || (CurrentClinicaId != null && entity.ClinicaId == CurrentClinicaId));
    }
}
