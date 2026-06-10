using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Data;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Perfil> Perfis { get; }

    DbSet<Paciente> Pacientes { get; }

    DbSet<Hospital> Hospitais { get; }

    DbSet<Convenio> Convenios { get; }

    DbSet<PacienteArquivo> PacienteArquivos { get; }

    DbSet<PacienteProcedimento> PacienteProcedimentos { get; }

    DbSet<UserArquivo> UserArquivos { get; }

    DbSet<CbhpmGeral> CbhpmGeral { get; }

    DbSet<Licenca> Licencas { get; }

    DbSet<Event> Events { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
