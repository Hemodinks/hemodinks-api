using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
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

    public static async Task<ResolvedMedico> ResolveMedicoAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        string currentUserName,
        int? medicoUserId,
        string? medicoNome,
        CancellationToken cancellationToken)
    {
        if (currentPerfilId == Perfil.MedicosId)
        {
            var accessibleMedicalUsers = MedicalGroupScope.BuildScopedMedicalUsersQuery(context, currentPerfilId, currentUserId, onlyActive: false);

            if (!medicoUserId.HasValue && string.IsNullOrWhiteSpace(medicoNome))
            {
                return new ResolvedMedico(currentUserId, currentUserName);
            }

            if (medicoUserId.HasValue)
            {
                var scopedMedico = await accessibleMedicalUsers
                    .Where(user => user.Id == medicoUserId.Value)
                    .Select(user => new { user.Id, user.Nome })
                    .FirstOrDefaultAsync(cancellationToken);

                if (scopedMedico == null)
                {
                    throw new InvalidOperationException("Medico invalido para o grupo do usuario.");
                }

                return new ResolvedMedico(scopedMedico.Id, scopedMedico.Nome);
            }

            var medicoNomeNormalizado = TrimOptional(medicoNome);
            var scopedMedicoPorNome = await accessibleMedicalUsers
                .Where(user => user.Nome == medicoNomeNormalizado)
                .Select(user => new { user.Id, user.Nome })
                .FirstOrDefaultAsync(cancellationToken);

            if (scopedMedicoPorNome == null)
            {
                throw new InvalidOperationException("Medico invalido para o grupo do usuario.");
            }

            return new ResolvedMedico(scopedMedicoPorNome.Id, scopedMedicoPorNome.Nome);
        }

        var nome = TrimOptional(medicoNome);

        if (medicoUserId.HasValue)
        {
            var medico = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == medicoUserId.Value && user.PerfilId == Perfil.MedicosId)
                .Select(user => new { user.Id, user.Nome })
                .FirstOrDefaultAsync(cancellationToken);

            if (medico == null)
            {
                throw new InvalidOperationException("Medico invalido");
            }

            return new ResolvedMedico(medico.Id, medico.Nome);
        }

        if (nome == null)
        {
            return new ResolvedMedico(null, null);
        }

        var medicoPorNome = await context.Users
            .AsNoTracking()
            .Where(user => user.Nome == nome && user.PerfilId == Perfil.MedicosId)
            .Select(user => new { user.Id, user.Nome })
            .FirstOrDefaultAsync(cancellationToken);

        if (medicoPorNome == null)
        {
            throw new InvalidOperationException("Medico invalido");
        }

        return new ResolvedMedico(medicoPorNome.Id, medicoPorNome.Nome);
    }

    public static Task<ResolvedMedico> ResolveOptionalMedicoAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        int? medicoUserId,
        string? medicoNome,
        CancellationToken cancellationToken)
    {
        if (!medicoUserId.HasValue && string.IsNullOrWhiteSpace(medicoNome))
        {
            return Task.FromResult(new ResolvedMedico(null, null));
        }

        return ResolveMedicoAsync(
            context,
            currentPerfilId,
            currentUserId,
            string.Empty,
            medicoUserId,
            medicoNome,
            cancellationToken);
    }

    public static void ValidateDistinctMedicos(params ResolvedMedico[] medicos)
    {
        var selectedIds = new HashSet<int>();
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var medico in medicos)
        {
            if (medico.UserId.HasValue)
            {
                if (!selectedIds.Add(medico.UserId.Value))
                {
                    throw new InvalidOperationException("Cirurgiao e medicos auxiliares devem ser diferentes");
                }

                continue;
            }

            if (medico.Nome != null && !selectedNames.Add(medico.Nome))
            {
                throw new InvalidOperationException("Cirurgiao e medicos auxiliares devem ser diferentes");
            }
        }
    }
}
