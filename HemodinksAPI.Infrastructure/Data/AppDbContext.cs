using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Infrastructure.Data.Configurations;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto de banco de dados da aplicação
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
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

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Configuração do modelo de dados
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
