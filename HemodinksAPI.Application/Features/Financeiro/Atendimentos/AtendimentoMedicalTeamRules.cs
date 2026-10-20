using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

internal static class AtendimentoMedicalTeamRules
{
    public static async Task ValidateAsync(IFinanceFeatureDbContext db, int? principal, int? first,
        int? second, int currentPerfilId, int currentUserId, CancellationToken ct)
    {
        SurgicalTeamRules.Validate(principal, first, second);
        var ids = new[] { principal, first, second }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (currentPerfilId == Perfil.MedicosId && !ids.Contains(currentUserId))
            throw new UnauthorizedAccessException("O medico deve participar da equipe cirurgica.");
        if (await db.Users.CountAsync(user => ids.Contains(user.Id) && user.PerfilId == Perfil.MedicosId && user.Ativo, ct) != ids.Length)
            throw new InvalidOperationException("Selecione apenas medicos ativos da clinica.");
    }
}
