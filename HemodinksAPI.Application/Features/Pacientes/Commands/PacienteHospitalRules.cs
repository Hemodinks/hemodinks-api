using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedHospital> ResolveHospitalAsync(
        IAppDbContext context,
        int? hospitalId,
        string? hospitalNome,
        CancellationToken cancellationToken)
    {
        Hospital? hospital = null;

        if (hospitalId.HasValue)
        {
            hospital = await context.Hospitais
                .FirstOrDefaultAsync(item => item.Id == hospitalId.Value, cancellationToken);
        }
        else
        {
            var nome = TrimAndValidateOptional(hospitalNome, 255, "Hospital excede 255 caracteres");
            if (nome == null)
            {
                throw new InvalidOperationException("Hospital invalido");
            }

            hospital = await context.Hospitais
                .FirstOrDefaultAsync(item => item.Nome == nome, cancellationToken);

            if (hospital == null)
            {
                hospital = new Hospital { Nome = nome };
                context.Hospitais.Add(hospital);
            }
        }

        if (hospital == null)
        {
            throw new InvalidOperationException("Hospital invalido");
        }

        return new ResolvedHospital(hospital.Id, hospital.Nome, hospital);
    }
}
