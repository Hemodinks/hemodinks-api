using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Data;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDataExecutionStrategy
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

public interface IDataTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IDataTransactionManager
{
    Task<IDataTransaction?> BeginAsync(CancellationToken cancellationToken);
}

public interface IClinicDirectoryDbContext
{
    DbSet<Clinica> Clinicas { get; }
}

public interface IGlobalIdentityDbContext : IUnitOfWork
{
    DbSet<UsuarioGlobal> UsuariosGlobais { get; }
    DbSet<UsuarioClinica> UsuariosClinicas { get; }
}

public interface IUserDbContext
{
    DbSet<User> Users { get; }
}

public interface IUserSearchDbContext : IUserDbContext
{
    DbSet<EquipeMembro> EquipeMembros { get; }
}

public interface IEquipeDirectoryDbContext
{
    DbSet<Equipe> Equipes { get; }
}

public interface IProfileDirectoryDbContext
{
    DbSet<Perfil> Perfis { get; }
}

public interface ITeamDbContext : IGlobalIdentityDbContext, IUserSearchDbContext, IEquipeDirectoryDbContext
{
    DbSet<EquipeOperador> EquipeOperadores { get; }
    DbSet<EquipeLoginDesafio> EquipeLoginDesafios { get; }
}

public interface IPlatformTeamDbContext : ITeamDbContext, IClinicDirectoryDbContext;

public interface IAuditDbContext
{
    DbSet<AuditoriaPlataforma> AuditoriasPlataforma { get; }
}

public interface IPlatformClinicDbContext : IPlatformTeamDbContext, IAuditDbContext
{
    DbSet<ConfiguracaoSistema> ConfiguracoesSistema { get; }
    DbSet<Convenio> Convenios { get; }
    DbSet<Hospital> Hospitais { get; }
    DbSet<Opme> OPME { get; }
}

public interface ISessionDbContext : IGlobalIdentityDbContext, IClinicDirectoryDbContext, IUserDbContext;

public interface ILegalAcceptanceDbContext : IUnitOfWork
{
    DbSet<UserLegalAcceptance> UserLegalAcceptances { get; }
}

public interface IPrivacyPreferenceDbContext : IUnitOfWork
{
    DbSet<UserPrivacyPreference> UserPrivacyPreferences { get; }
}

public interface IFinanceEndpointDbContext : IUnitOfWork, IAuditDbContext
{
    DbSet<AtendimentoCirurgico> AtendimentosCirurgicos { get; }
    DbSet<AtendimentoArquivo> AtendimentoArquivos { get; }
    DbSet<FaturamentoHistoricoArquivo> FaturamentoHistoricoArquivos { get; }
    DbSet<Recebimento> Recebimentos { get; }
}

public interface IClinicalReferenceDbContext
{
    DbSet<CbhpmGeral> CbhpmGeral { get; }
    DbSet<Convenio> Convenios { get; }
    DbSet<Hospital> Hospitais { get; }
    DbSet<Opme> OPME { get; }
}

public interface IPatientDataDbContext : IUserDbContext
{
    DbSet<Paciente> Pacientes { get; }
    DbSet<PacienteArquivo> PacienteArquivos { get; }
    DbSet<PacienteProcedimento> PacienteProcedimentos { get; }
    DbSet<Observacao> Observacoes { get; }
    DbSet<FaturamentoMedico> FaturamentosMedicos { get; }
}

public interface IMedicalGroupDataDbContext : IUserDbContext
{
    DbSet<GrupoMedico> GruposMedicos { get; }
    DbSet<GrupoMedicoUsuario> GrupoMedicoUsuarios { get; }
}

public interface IMedicalUserScopeDbContext :
    IUserSearchDbContext,
    IEquipeDirectoryDbContext,
    IMedicalGroupDataDbContext;

public interface IEventDataDbContext
{
    DbSet<Event> Events { get; }
    DbSet<AgendaNotification> AgendaNotifications { get; }
}

public interface IFinancialDataDbContext : IFinanceEndpointDbContext
{
    DbSet<AtendimentoProcedimento> AtendimentoProcedimentos { get; }
    DbSet<Faturamento> Faturamentos { get; }
    DbSet<FaturamentoItem> FaturamentoItens { get; }
    DbSet<Glosa> Glosas { get; }
    DbSet<RecursoGlosa> RecursosGlosa { get; }
    DbSet<ContaReceber> ContasReceber { get; }
    DbSet<ConvenioProcedimentoPreco> ConvenioProcedimentoPrecos { get; }
    DbSet<FinanceiroMigracaoInconsistencia> FinanceiroMigracaoInconsistencias { get; }
}

public interface IDashboardFinancialReadDbContext
{
    DbSet<AtendimentoCirurgico> AtendimentosCirurgicos { get; }
    DbSet<Faturamento> Faturamentos { get; }
    DbSet<ContaReceber> ContasReceber { get; }
}

public interface IUserAdministrationDataDbContext : IPasswordResetDbContext
{
    DbSet<Perfil> Perfis { get; }
    DbSet<UserArquivo> UserArquivos { get; }
    DbSet<Licenca> Licencas { get; }
}

public interface IPasswordResetDbContext : IUnitOfWork
{
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
}

public interface IPasswordCredentialDbContext : IUnitOfWork, IUserDbContext, IGlobalIdentityDbContext;

public interface IPasswordResetOperationsDbContext :
    IPasswordCredentialDbContext,
    IPasswordResetDbContext;

public interface IPlatformPasswordResetDbContext : IPasswordResetOperationsDbContext;

public interface IUserFeatureDbContext :
    IUnitOfWork,
    ITeamDbContext,
    IClinicDirectoryDbContext,
    IPatientDataDbContext,
    IMedicalUserScopeDbContext,
    IUserAdministrationDataDbContext;

public interface IPatientFeatureDbContext :
    IUnitOfWork,
    IPatientDataDbContext,
    IMedicalUserScopeDbContext,
    IGlobalIdentityDbContext,
    IClinicalReferenceDbContext,
    IPasswordResetDbContext;

public interface ICbhpmFeatureDbContext : IUnitOfWork, IClinicalReferenceDbContext;

public interface IMedicalGroupFeatureDbContext :
    IUnitOfWork,
    IMedicalUserScopeDbContext;

public interface IEventFeatureDbContext :
    IUnitOfWork,
    IMedicalUserScopeDbContext,
    IEventDataDbContext;

public interface IFinanceFeatureDbContext :
    IUnitOfWork,
    IFinancialDataDbContext,
    IPatientDataDbContext,
    IClinicalReferenceDbContext;

public interface IFaturamentoMedicoFeatureDbContext :
    IUnitOfWork,
    IPatientDataDbContext,
    IMedicalUserScopeDbContext,
    IClinicalReferenceDbContext;

public interface IDashboardFeatureDbContext :
    IPatientDataDbContext,
    IMedicalUserScopeDbContext,
    IEventDataDbContext,
    IDashboardFinancialReadDbContext;

public interface ILicensingFeatureDbContext :
    IUnitOfWork,
    IUserDbContext,
    IClinicDirectoryDbContext
{
    DbSet<Licenca> Licencas { get; }
}

public interface ICatalogQueryDbContext : IClinicalReferenceDbContext;
