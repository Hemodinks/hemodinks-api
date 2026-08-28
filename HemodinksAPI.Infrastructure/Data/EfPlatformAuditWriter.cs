using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class EfPlatformAuditWriter(PlatformDbContext context) : IPlatformAuditWriter
{
    public async Task WriteAsync(PlatformAuditEntry entry, CancellationToken cancellationToken)
    {
        context.AuditoriasPlataforma.Add(new AuditoriaPlataforma
        {
            UsuarioGlobalId = entry.UsuarioGlobalId,
            ClinicaId = entry.ClinicaId,
            UserId = entry.UserId,
            Acao = entry.Acao,
            Recurso = entry.Recurso,
            EntidadeId = entry.EntidadeId,
            DetalhesJson = entry.DetalhesJson,
            Ip = entry.Ip,
            UserAgent = entry.UserAgent,
            RequestId = entry.RequestId,
            Sucesso = entry.Sucesso,
            DataCadastro = entry.DataCadastro
        });

        await context.SaveChangesAsync(cancellationToken);
    }
}
