using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

internal static class PacienteObservacaoAccess
{
    public static async Task<PacienteObservacaoContext> GetPacienteContextAsync(
        IAppDbContext context,
        int pacienteId,
        int currentPerfilId,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var query = PacienteAccess.ApplyScope(
            context,
            context.Pacientes.AsNoTracking(),
            currentPerfilId,
            currentUserId);

        var paciente = await query
            .Where(item => item.Id == pacienteId)
            .Select(item => new PacienteObservacaoContext(
                item.Id,
                item.NomePaciente,
                item.MedicoUserId,
                item.MedicoUser != null ? item.MedicoUser.Nome : item.Medico,
                item.MedicoAuxiliar1UserId,
                item.MedicoAuxiliar1User != null ? item.MedicoAuxiliar1User.Nome : item.MedicoAuxiliar1,
                item.MedicoAuxiliar2UserId,
                item.MedicoAuxiliar2User != null ? item.MedicoAuxiliar2User.Nome : item.MedicoAuxiliar2))
            .FirstOrDefaultAsync(cancellationToken);

        if (paciente == null)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar as observacoes deste paciente.");
        }

        return paciente;
    }
}

internal sealed record PacienteObservacaoContext(
    int Id,
    string NomePaciente,
    int? MedicoUserId,
    string? Medico,
    int? MedicoAuxiliar1UserId,
    string? MedicoAuxiliar1,
    int? MedicoAuxiliar2UserId,
    string? MedicoAuxiliar2);
