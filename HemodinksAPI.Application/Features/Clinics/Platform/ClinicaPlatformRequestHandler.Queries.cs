using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        public async Task<PlatformUseCaseResult> ListClinicas(
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var query = context.Clinicas.AsNoTracking();
        if (requestContext.PerfilId == Perfil.AdministradorId)
        {
        if (!requestContext.ClinicaId.HasValue)
        {
        return PlatformUseCaseResult.Forbidden();
        }
        
        query = query.Where(item => item.Id == requestContext.ClinicaId.Value);
        }
        
        var clinicas = await query
        .AsNoTracking()
        .OrderBy(item => item.Nome)
        .ToListAsync(cancellationToken);
        var userCounts = await ClinicEmployees(context)
        .AsNoTracking()
        .GroupBy(item => item.ClinicaId)
        .Select(group => new { ClinicaId = group.Key, Count = group.Count() })
        .ToDictionaryAsync(item => item.ClinicaId, item => item.Count, cancellationToken);
        var items = clinicas
        .Select(item => ToResponse(item, userCounts.GetValueOrDefault(item.Id)))
        .ToList();
        
        return PlatformUseCaseResult.Ok(items);
        }

        public async Task<PlatformUseCaseResult> GetClinica(
        int id,
        CancellationToken cancellationToken)
        {
        var clinica = await context.Clinicas.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (clinica == null)
        {
        return PlatformUseCaseResult.NotFound();
        }
        
        var userCount = await ClinicEmployees(context)
        .CountAsync(item => item.ClinicaId == id, cancellationToken);
        return PlatformUseCaseResult.Ok(ToResponse(clinica, userCount));
        }

        private static ClinicaPlatformResponse ToResponse(Clinica clinica, int? userCount)
        {
        return new ClinicaPlatformResponse(
        clinica.Id,
        clinica.Nome,
        clinica.Slug,
        clinica.Cnpj,
        clinica.FotoClinica == null ? null : $"/api/public/clinicas/{clinica.Slug}/foto",
        clinica.Ativa,
        clinica.Plano,
        ClinicaModulos.GetEffective(clinica.Plano, clinica.ModulosLiberados),
        clinica.AssinaturaStatus,
        clinica.TrialAte,
        clinica.AssinaturaValidaAte,
        clinica.LimiteUsuarios,
        userCount,
        clinica.DataCadastro,
        clinica.DataAtualizacao);
        }
}
