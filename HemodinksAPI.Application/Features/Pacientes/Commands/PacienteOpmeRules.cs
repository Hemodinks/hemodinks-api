using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedOpmeFornecedor?> ResolveOpmeFornecedorAsync(
        IPatientFeatureDbContext context,
        int clinicaId,
        int? fornecedorId,
        string? fornecedorNome,
        CancellationToken cancellationToken)
    {
        return await ClinicalReferenceResolver.ResolveOpmeFornecedorAsync(
            context, clinicaId, fornecedorId, fornecedorNome, cancellationToken);
    }
}
