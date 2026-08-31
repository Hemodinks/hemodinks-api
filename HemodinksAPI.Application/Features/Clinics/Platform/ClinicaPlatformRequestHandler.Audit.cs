using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        public async Task<PlatformUseCaseResult> ListPlatformAudit(
        int? clinicaId,
        string? acao,
        DateTime? de,
        DateTime? ate,
        int pagina = 1,
        int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
        {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 200);
        
        var query = context.AuditoriasPlataforma.AsNoTracking().AsQueryable();
        if (clinicaId.HasValue) query = query.Where(item => item.ClinicaId == clinicaId.Value);
        if (!string.IsNullOrWhiteSpace(acao)) query = query.Where(item => item.Acao == acao.Trim());
        if (de.HasValue) query = query.Where(item => item.DataCadastro >= de.Value);
        if (ate.HasValue) query = query.Where(item => item.DataCadastro <= ate.Value);
        
        var total = await query.CountAsync(cancellationToken);
        var items = await query
        .OrderByDescending(item => item.DataCadastro)
        .Skip((pagina - 1) * tamanhoPagina)
        .Take(tamanhoPagina)
        .Select(item => new
        {
        item.Id,
        item.UsuarioGlobalId,
        item.ClinicaId,
        item.UserId,
        item.Acao,
        item.Recurso,
        item.EntidadeId,
        item.DetalhesJson,
        item.Ip,
        item.UserAgent,
        item.RequestId,
        item.Sucesso,
        item.DataCadastro
        })
        .ToListAsync(cancellationToken);
        
        return PlatformUseCaseResult.Ok(new { pagina, tamanhoPagina, total, items });
        }
}
