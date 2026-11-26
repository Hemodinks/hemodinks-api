using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Auditing;

// #368: ClinicaId nao faz parte da entrada; o writer usa a clinica do contexto autenticado.
public sealed record ClinicAuditEntry(
    int? UsuarioId,
    int? EquipeOperadorId,
    string Acao,
    string Recurso,
    string? RecursoId,
    int StatusCode,
    string? IpTruncado,
    DateTime DataUtc,
    int? PacienteId = null,
    string? Detalhe = null);

public interface IClinicAuditWriter
{
    Task WriteAsync(ClinicAuditEntry entry, CancellationToken cancellationToken);
}

// Leitura da trilha sempre sob o filtro de tenant (AppDbContext da requisicao).
public interface IClinicAuditDbContext
{
    DbSet<AuditoriaClinica> AuditoriasClinica { get; }
    // So para exibir nomes na consulta (sob o mesmo filtro de tenant); a trilha guarda apenas ids.
    DbSet<User> Users { get; }
    DbSet<EquipeOperador> EquipeOperadores { get; }
    // So para exibir o nome do paciente e aplicar os filtros da tela de Pacientes (mesmo filtro de tenant).
    DbSet<Paciente> Pacientes { get; }
    DbSet<ConfiguracaoSistema> ConfiguracoesSistema { get; }
}
