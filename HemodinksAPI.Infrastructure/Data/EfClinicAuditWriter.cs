using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

// #368: grava num AppDbContext proprio (mesmo tenant da requisicao) para nunca persistir, junto
// com a auditoria, alteracoes pendentes de uma operacao que falhou no contexto da requisicao.
public sealed class EfClinicAuditWriter(
    DbContextOptions<AppDbContext> options,
    IClinicaContext tenant,
    ICpfProtector? cpfProtector = null) : IClinicAuditWriter
{
    public async Task WriteAsync(ClinicAuditEntry entry, CancellationToken cancellationToken)
    {
        var clinicId = tenant.GetRequiredClinicaId();
        await using var context = new AppDbContext(options, tenant, cpfProtector);
        context.AuditoriasClinica.Add(new AuditoriaClinica
        {
            ClinicaId = clinicId,
            UsuarioId = entry.UsuarioId,
            EquipeOperadorId = entry.EquipeOperadorId,
            Acao = entry.Acao,
            Recurso = entry.Recurso,
            RecursoId = entry.RecursoId,
            StatusCode = entry.StatusCode,
            Sucesso = entry.StatusCode is >= 200 and < 400,
            IpTruncado = entry.IpTruncado,
            DataUtc = entry.DataUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
