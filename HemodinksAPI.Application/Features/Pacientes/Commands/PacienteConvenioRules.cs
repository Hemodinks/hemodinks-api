using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedConvenio?> ResolveConvenioAsync(
        IAppDbContext context,
        int? convenioId,
        string? convenioDescricao,
        CancellationToken cancellationToken)
    {
        return await ClinicalReferenceResolver.ResolveConvenioAsync(
            context, convenioId, convenioDescricao, cancellationToken);
    }
}
