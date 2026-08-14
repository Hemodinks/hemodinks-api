using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedOpmeFornecedor?> ResolveOpmeFornecedorAsync(
        IAppDbContext context,
        int? fornecedorId,
        string? fornecedorNome,
        CancellationToken cancellationToken)
    {
        return await ClinicalReferenceResolver.ResolveOpmeFornecedorAsync(
            context, fornecedorId, fornecedorNome, cancellationToken);
    }
}
