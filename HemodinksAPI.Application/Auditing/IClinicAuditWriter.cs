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
    DateTime DataUtc);

public interface IClinicAuditWriter
{
    Task WriteAsync(ClinicAuditEntry entry, CancellationToken cancellationToken);
}

// Leitura da trilha sempre sob o filtro de tenant (AppDbContext da requisicao).
public interface IClinicAuditDbContext
{
    DbSet<AuditoriaClinica> AuditoriasClinica { get; }
}
