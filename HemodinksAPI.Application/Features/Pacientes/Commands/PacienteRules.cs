using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static class PacienteRules
{
    public static void ValidateNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
        }
    }

    public static async Task<string?> NormalizeAndValidateCpfAsync(
        IAppDbContext context,
        string? cpf,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return null;
        }

        if (!CpfUtils.IsValid(cpf))
        {
            throw new InvalidOperationException("CPF invalido");
        }

        var normalizedCpf = CpfUtils.Normalize(cpf)!;
        var cpfAlreadyExists = await context.Users
            .AnyAsync(u => u.Cpf == normalizedCpf && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (cpfAlreadyExists)
        {
            throw new InvalidOperationException("CPF ja cadastrado");
        }

        return normalizedCpf;
    }

    public static async Task<string> ResolveEmailAsync(
        IAppDbContext context,
        string? email,
        string? cpf,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        var resolvedEmail = string.IsNullOrWhiteSpace(email)
            ? GenerateTechnicalEmail(cpf)
            : email.Trim();

        var emailAlreadyExists = await context.Users
            .AnyAsync(u => u.Email == resolvedEmail && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (emailAlreadyExists)
        {
            throw new InvalidOperationException("Email ja cadastrado");
        }

        return resolvedEmail;
    }

    public static string ResolveTelefone(string? telefone)
    {
        return TrimOptional(telefone) ?? string.Empty;
    }

    private static string GenerateTechnicalEmail(string? cpf)
    {
        return !string.IsNullOrWhiteSpace(cpf)
            ? $"paciente-{cpf}@hemodinks.local"
            : $"paciente-{Guid.NewGuid():N}@hemodinks.local";
    }

    public static async Task ValidateEmailAsync(
        IAppDbContext context,
        string email,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email obrigatorio");
        }

        var trimmedEmail = email.Trim();
        var emailAlreadyExists = await context.Users
            .AnyAsync(u => u.Email == trimmedEmail && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (emailAlreadyExists)
        {
            throw new InvalidOperationException("Email ja cadastrado");
        }
    }

    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? TrimAndValidateOptional(string? value, int maxLength, string errorMessage)
    {
        var trimmed = TrimOptional(value);
        if (trimmed?.Length > maxLength)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return trimmed;
    }

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

    public static async Task<List<ResolvedProcedimento>> ResolveProcedimentosAsync(
        ICbhpmCache cbhpmCache,
        IEnumerable<PacienteProcedimentoCommandDto>? procedimentos,
        string? cbhpmCodigo,
        string? procedimento,
        string? cbhpmPorte,
        CancellationToken cancellationToken)
    {
        var requestedItems = procedimentos?
            .Where(item => item != null)
            .ToList() ?? [];

        if (requestedItems.Count == 0)
        {
            requestedItems =
            [
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = cbhpmCodigo,
                    CbhpmPorte = cbhpmPorte,
                    Procedimento = procedimento
                }
            ];
        }

        var resolvedItems = new List<ResolvedProcedimento>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in requestedItems)
        {
            var resolved = await ResolveProcedimentoItemAsync(cbhpmCache, item, cancellationToken);
            if (resolved == null)
            {
                continue;
            }

            var key = resolved.Codigo != null
                ? $"codigo:{resolved.Codigo}"
                : $"livre:{resolved.Nome}|{resolved.Porte}";

            if (seenKeys.Add(key))
            {
                resolvedItems.Add(resolved);
            }
        }

        return resolvedItems;
    }

    public static List<PacienteProcedimento> ToPacienteProcedimentos(IReadOnlyList<ResolvedProcedimento> procedimentos)
    {
        return procedimentos
            .Select((procedimento, index) => new PacienteProcedimento
            {
                CbhpmCodigo = procedimento.Codigo,
                CbhpmPorte = procedimento.Porte,
                Procedimento = procedimento.Nome,
                ValorReferencia = procedimento.ValorReferencia,
                Ordem = index + 1
            })
            .ToList();
    }

    private static async Task<ResolvedProcedimento?> ResolveProcedimentoItemAsync(
        ICbhpmCache cbhpmCache,
        PacienteProcedimentoCommandDto item,
        CancellationToken cancellationToken)
    {
        var codigo = CbhpmCodigoUtils.NormalizeOptional(item.CbhpmCodigo);
        var procedimento = TrimOptional(item.Procedimento);
        var porte = TrimOptional(item.CbhpmPorte);

        if (codigo == null)
        {
            if (procedimento == null)
            {
                return null;
            }

            ValidateManualProcedimento(procedimento, porte);

            return new ResolvedProcedimento(null, procedimento, porte, item.ValorReferencia);
        }

        if (codigo.Length > 20)
        {
            throw new InvalidOperationException("Codigo CBHPM invalido");
        }

        var cbhpm = await cbhpmCache.GetByCodigoAsync(codigo, cancellationToken);

        if (cbhpm != null)
        {
            return new ResolvedProcedimento(CbhpmCodigoUtils.Normalize(cbhpm.Codigo), cbhpm.Procedimento, cbhpm.Porte, cbhpm.ValorReferencia);
        }

        if (procedimento == null)
        {
            throw new InvalidOperationException("Informe a descricao do procedimento para o codigo CBHPM nao cadastrado");
        }

        ValidateManualProcedimento(procedimento, porte);

        return new ResolvedProcedimento(codigo, procedimento, porte, item.ValorReferencia);
    }

    private static void ValidateManualProcedimento(string procedimento, string? porte)
    {
        if (procedimento.Length > 1000)
        {
            throw new InvalidOperationException("Procedimento excede 1000 caracteres");
        }

        if (porte?.Length > 10)
        {
            throw new InvalidOperationException("Porte CBHPM invalido");
        }
    }
}
