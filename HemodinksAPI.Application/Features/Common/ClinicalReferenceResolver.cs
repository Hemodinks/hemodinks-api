using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Common;

internal sealed record ResolvedHospital(int Id, string Nome, Hospital Referencia);

internal sealed record ResolvedConvenio(int Id, string Descricao, Convenio Referencia);

internal sealed record ResolvedOpmeFornecedor(
    int Id,
    string Fornecedor,
    HemodinksAPI.Domain.Models.Opme FornecedorReferencia);

internal static class ClinicalReferenceResolver
{
    public static async Task<ResolvedHospital> ResolveHospitalAsync(
        IAppDbContext context,
        int clinicaId,
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
                throw new InvalidOperationException("Hospital invalido");

            hospital = await context.Hospitais
                .FirstOrDefaultAsync(item => item.Nome == nome, cancellationToken);

            if (hospital == null)
            {
                hospital = new Hospital { ClinicaId = clinicaId, Nome = nome };
                context.Hospitais.Add(hospital);
            }
        }

        if (hospital == null)
            throw new InvalidOperationException("Hospital invalido");

        return new ResolvedHospital(hospital.Id, hospital.Nome, hospital);
    }

    public static async Task<ResolvedConvenio?> ResolveConvenioAsync(
        IAppDbContext context,
        int clinicaId,
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
                return null;

            convenio = await context.Convenios
                .FirstOrDefaultAsync(item => item.DescricaoConvenio == descricao, cancellationToken);

            if (convenio == null)
            {
                convenio = new Convenio { ClinicaId = clinicaId, DescricaoConvenio = descricao };
                context.Convenios.Add(convenio);
            }
        }

        if (convenio == null)
            throw new InvalidOperationException("Convenio invalido");

        return new ResolvedConvenio(convenio.IdConvenio, convenio.DescricaoConvenio, convenio);
    }

    public static async Task<ResolvedOpmeFornecedor?> ResolveOpmeFornecedorAsync(
        IAppDbContext context,
        int clinicaId,
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
                return null;

            fornecedor = await context.OPME
                .FirstOrDefaultAsync(item => item.Fornecedor == nome, cancellationToken);

            if (fornecedor == null)
            {
                fornecedor = new HemodinksAPI.Domain.Models.Opme
                {
                    ClinicaId = clinicaId,
                    Fornecedor = nome
                };
                context.OPME.Add(fornecedor);
            }
        }

        if (fornecedor == null)
            throw new InvalidOperationException("Fornecedor OPME invalido");

        return new ResolvedOpmeFornecedor(fornecedor.IdFornecedor, fornecedor.Fornecedor, fornecedor);
    }

    private static string? TrimAndValidateOptional(string? value, int maxLength, string errorMessage)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new InvalidOperationException(errorMessage);

        return normalized;
    }
}
