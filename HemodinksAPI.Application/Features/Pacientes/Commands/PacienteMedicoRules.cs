using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<ResolvedMedico> ResolveMedicoAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        string currentUserName,
        int? currentEquipeId,
        int? medicoUserId,
        string? medicoNome,
        CancellationToken cancellationToken)
    {
        if (currentPerfilId == Perfil.MedicosId || currentPerfilId == Perfil.EquipeId)
        {
            var accessibleMedicalUsers = MedicalGroupScope.BuildScopedMedicalUsersQuery(
                context, currentPerfilId, currentUserId, currentEquipeId, onlyActive: false);

            if (currentPerfilId == Perfil.MedicosId && !medicoUserId.HasValue && string.IsNullOrWhiteSpace(medicoNome))
            {
                return new ResolvedMedico(currentUserId, currentUserName);
            }

            if (!medicoUserId.HasValue && string.IsNullOrWhiteSpace(medicoNome))
            {
                throw new InvalidOperationException("Selecione um medico associado a equipe.");
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
            var medico = await MedicalGroupScope.BuildScopedMedicalUsersQuery(
                    context, currentPerfilId, currentUserId, currentEquipeId, onlyActive: false)
                .Where(user => user.Id == medicoUserId.Value)
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

        var medicoPorNome = await MedicalGroupScope.BuildScopedMedicalUsersQuery(
                context, currentPerfilId, currentUserId, currentEquipeId, onlyActive: false)
            .Where(user => user.Nome == nome)
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
        int? currentEquipeId,
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
            currentEquipeId,
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
