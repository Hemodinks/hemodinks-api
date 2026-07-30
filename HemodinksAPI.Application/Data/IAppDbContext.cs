using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Data;

public interface IAppDbContext
{
    DbSet<Clinica> Clinicas { get; }

    DbSet<UsuarioGlobal> UsuariosGlobais { get; }

    DbSet<UsuarioClinica> UsuariosClinicas { get; }

    DbSet<AuditoriaPlataforma> AuditoriasPlataforma { get; }

    DbSet<User> Users { get; }

    DbSet<Perfil> Perfis { get; }

    DbSet<Paciente> Pacientes { get; }

    DbSet<FaturamentoMedico> FaturamentosMedicos { get; }

    DbSet<AtendimentoCirurgico> AtendimentosCirurgicos { get; }
    DbSet<AtendimentoProcedimento> AtendimentoProcedimentos { get; }
    DbSet<AtendimentoArquivo> AtendimentoArquivos { get; }
    DbSet<Faturamento> Faturamentos { get; }
    DbSet<FaturamentoItem> FaturamentoItens { get; }
    DbSet<Glosa> Glosas { get; }
    DbSet<RecursoGlosa> RecursosGlosa { get; }
    DbSet<ContaReceber> ContasReceber { get; }
    DbSet<Recebimento> Recebimentos { get; }
    DbSet<ConvenioProcedimentoPreco> ConvenioProcedimentoPrecos { get; }
    DbSet<FinanceiroMigracaoInconsistencia> FinanceiroMigracaoInconsistencias { get; }

    DbSet<Observacao> Observacoes { get; }

    DbSet<GrupoMedico> GruposMedicos { get; }

    DbSet<GrupoMedicoUsuario> GrupoMedicoUsuarios { get; }

    DbSet<Hospital> Hospitais { get; }

    DbSet<Convenio> Convenios { get; }

    DbSet<Opme> OPME { get; }

    DbSet<PacienteArquivo> PacienteArquivos { get; }

    DbSet<PacienteProcedimento> PacienteProcedimentos { get; }

    DbSet<UserArquivo> UserArquivos { get; }

    DbSet<CbhpmGeral> CbhpmGeral { get; }

    DbSet<Licenca> Licencas { get; }

    DbSet<Event> Events { get; }

    DbSet<AgendaNotification> AgendaNotifications { get; }

    DbSet<IdempotencyRequest> IdempotencyRequests { get; }

    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    DbSet<ConfiguracaoSistema> ConfiguracoesSistema { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
