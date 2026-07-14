using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedConvenio?> ResolveConvenioAsync(
        IAppDbContext context,
        int? convenioId,
        string? convenioDescricao,
        CancellationToken cancellationToken)
    {
        Convenio? convenio = null;

        if (convenioId.HasValue)
        {
            convenio = await context.Convenios
                .FirstOrDefaultAsync(item => item.IdConvenio == convenioId.Value, cancellationToken);
        }
        else
        {
            var descricao = TrimAndValidateOptional(convenioDescricao, 255, "Convenio excede 255 caracteres");
            if (descricao == null)
            {
                return null;
            }

            convenio = await context.Convenios
                .FirstOrDefaultAsync(item => item.DescricaoConvenio == descricao, cancellationToken);

            if (convenio == null)
            {
                convenio = new Convenio { DescricaoConvenio = descricao };
                context.Convenios.Add(convenio);
            }
        }

        if (convenio == null)
        {
            throw new InvalidOperationException("Convenio invalido");
        }

        return new ResolvedConvenio(convenio.IdConvenio, convenio.DescricaoConvenio, convenio);
    }
}
