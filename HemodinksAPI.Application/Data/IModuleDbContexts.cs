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

public interface ITeamDbContext : IGlobalIdentityDbContext, IUserDbContext
{
    DbSet<Equipe> Equipes { get; }
    DbSet<EquipeMembro> EquipeMembros { get; }
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

public interface IFinanceEndpointDbContext : IUnitOfWork, IAuditDbContext
{
    DbSet<AtendimentoCirurgico> AtendimentosCirurgicos { get; }
    DbSet<AtendimentoArquivo> AtendimentoArquivos { get; }
    DbSet<Recebimento> Recebimentos { get; }
}
