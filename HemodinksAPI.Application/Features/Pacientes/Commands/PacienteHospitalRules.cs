using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedHospital> ResolveHospitalAsync(
        IPatientFeatureDbContext context,
        int clinicaId,
        int? hospitalId,
        string? hospitalNome,
        CancellationToken cancellationToken)
    {
        return await ClinicalReferenceResolver.ResolveHospitalAsync(
            context, clinicaId, hospitalId, hospitalNome, cancellationToken);
    }
}
