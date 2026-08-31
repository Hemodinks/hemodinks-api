using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        public async Task<PlatformUseCaseResult> DeactivateClinica(
        int id,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var currentClinicId = requestContext.ClinicaId.GetValueOrDefault();
        if (currentClinicId == id)
        {
        return PlatformUseCaseResult.Conflict(new { message = "Troque para outra clinica antes de desativar a clinica atual." });
        }
        
        var clinica = await context.Clinicas.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (clinica == null)
        {
        return PlatformUseCaseResult.NotFound();
        }
        
        clinica.Ativa = false;
        clinica.AssinaturaStatus = ClinicaAssinaturaStatus.Cancelada;
        clinica.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
        requestContext,
        "clinic.deactivate",
        "clinic",
        clinica.Id.ToString(),
        clinica.Id,
        new { clinica.Nome, clinica.Slug },
        true,
        cancellationToken);
        
        return PlatformUseCaseResult.NoContent();
        }
}
