using HemodinksAPI.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedOpmeFornecedor?> ResolveOpmeFornecedorAsync(
        IAppDbContext context,
        int? fornecedorId,
        string? fornecedorNome,
        CancellationToken cancellationToken)
    {
        HemodinksAPI.Domain.Models.Opme? fornecedor = null;

        if (fornecedorId.HasValue)
        {
            fornecedor = await context.OPME
                .FirstOrDefaultAsync(item => item.IdFornecedor == fornecedorId.Value, cancellationToken);
        }
        else
        {
            var nome = TrimAndValidateOptional(fornecedorNome, 255, "Fornecedor OPME excede 255 caracteres");
            if (nome == null)
            {
                return null;
            }

            fornecedor = await context.OPME
                .FirstOrDefaultAsync(item => item.Fornecedor == nome, cancellationToken);

            if (fornecedor == null)
            {
                fornecedor = new HemodinksAPI.Domain.Models.Opme { Fornecedor = nome };
                context.OPME.Add(fornecedor);
            }
        }

        if (fornecedor == null)
        {
            throw new InvalidOperationException("Fornecedor OPME invalido");
        }

        return new ResolvedOpmeFornecedor(fornecedor.IdFornecedor, fornecedor.Fornecedor, fornecedor);
    }
}
