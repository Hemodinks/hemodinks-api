using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public sealed record DataConsolidacaoPendenciasDto(int Total, int AnoMinimo, int AnoMaximo);

public sealed record GetDataConsolidacaoPendenciasQuery(int CurrentUserId, int CurrentPerfilId, int? CurrentEquipeId)
    : IRequest<DataConsolidacaoPendenciasDto>;

internal static class DataConsolidacaoPendencias
{
    // Perfis que recebem o aviso: médico (cirurgião ou auxiliar do cadastro), controller e administradores da clínica.
    public static bool CanView(int perfilId) =>
        Perfil.IsAdministradorOuSuper(perfilId) || perfilId == Perfil.ControllerId || perfilId == Perfil.MedicosId;

    // Aplicar sempre depois de PacienteAccess.ApplyScope (que já restringe à clínica e ao escopo do perfil).
    public static IQueryable<Paciente> Apply(IQueryable<Paciente> query, int perfilId, int userId, int anoMaximo)
    {
        if (!CanView(perfilId))
            return query.Where(_ => false);

        // O médico é avisado só dos cadastros em que é cirurgião, auxiliar 1 ou auxiliar 2,
        // e não dos demais pacientes do grupo médico.
        if (perfilId == Perfil.MedicosId)
        {
            query = query.Where(p => p.MedicoUserId == userId
                || p.MedicoAuxiliar1UserId == userId
                || p.MedicoAuxiliar2UserId == userId);
        }

        // Comparação por faixa (sem .Year) para aproveitar índice em DataAtendimento.
        var inicio = new DateTime(DataConsolidacaoRules.AnoMinimo, 1, 1);
        var fimExclusivo = new DateTime(anoMaximo + 1, 1, 1);
        return query.Where(p => p.DataAtendimento.HasValue
            && (p.DataAtendimento.Value < inicio || p.DataAtendimento.Value >= fimExclusivo));
    }
}

public sealed class GetDataConsolidacaoPendenciasQueryHandler(
    IPatientFeatureDbContext context,
    TimeProvider? timeProvider = null)
    : IRequestHandler<GetDataConsolidacaoPendenciasQuery, DataConsolidacaoPendenciasDto>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DataConsolidacaoPendenciasDto> Handle(GetDataConsolidacaoPendenciasQuery request, CancellationToken cancellationToken)
    {
        if (!DataConsolidacaoPendencias.CanView(request.CurrentPerfilId))
            throw new UnauthorizedAccessException("Sem permissao para consultar pendencias de pacientes");

        var anoMaximo = DataConsolidacaoRules.AnoMaximo(_timeProvider.GetUtcNow());
        var query = PacienteAccess.ApplyScope(context, context.Pacientes.AsNoTracking(),
            request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId);
        var total = await DataConsolidacaoPendencias
            .Apply(query, request.CurrentPerfilId, request.CurrentUserId, anoMaximo)
            .CountAsync(cancellationToken);
        return new DataConsolidacaoPendenciasDto(total, DataConsolidacaoRules.AnoMinimo, anoMaximo);
    }
}
