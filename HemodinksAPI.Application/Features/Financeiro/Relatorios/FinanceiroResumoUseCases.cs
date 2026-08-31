using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class ObterFinanceiroResumoQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ObterFinanceiroResumoQuery, FinanceiroResumoDto>
{
    public async Task<FinanceiroResumoDto> Handle(ObterFinanceiroResumoQuery request, CancellationToken ct)
    {
        var faturamentos = db.Faturamentos.AsNoTracking().AsQueryable(); var contas = db.ContasReceber.AsNoTracking().AsQueryable();
        if (request.Inicio.HasValue) { faturamentos = faturamentos.Where(x => x.Competencia >= request.Inicio); contas = contas.Where(x => x.Competencia >= request.Inicio); }
        if (request.Fim.HasValue) { faturamentos = faturamentos.Where(x => x.Competencia <= request.Fim); contas = contas.Where(x => x.Competencia <= request.Fim); }
        if (request.ConvenioId.HasValue) { faturamentos = faturamentos.Where(x => x.ConvenioId == request.ConvenioId); contas = contas.Where(x => x.ConvenioId == request.ConvenioId); }
        if (request.MedicoId.HasValue)
        {
            faturamentos = faturamentos.Where(x => x.AtendimentoCirurgico.MedicoResponsavelId == request.MedicoId || x.AtendimentoCirurgico.MedicoAuxiliar1Id == request.MedicoId || x.AtendimentoCirurgico.MedicoAuxiliar2Id == request.MedicoId);
            contas = contas.Where(x => x.Faturamento.AtendimentoCirurgico.MedicoResponsavelId == request.MedicoId || x.Faturamento.AtendimentoCirurgico.MedicoAuxiliar1Id == request.MedicoId || x.Faturamento.AtendimentoCirurgico.MedicoAuxiliar2Id == request.MedicoId);
        }
        if (request.PacienteId.HasValue) { faturamentos = faturamentos.Where(x => x.AtendimentoCirurgico.PacienteId == request.PacienteId); contas = contas.Where(x => x.PacienteId == request.PacienteId); }
        var f = await faturamentos.GroupBy(x => 1).Select(g => new { Apresentado = g.Sum(x => x.ValorApresentado), Glosado = g.Sum(x => x.ValorGlosado), Recuperado = g.Sum(x => x.ValorGlosaRecuperada), Reconhecido = g.Sum(x => x.ValorReconhecido) }).SingleOrDefaultAsync(ct);
        var c = await contas.Where(x => x.Status != ContaReceberStatus.Cancelado).GroupBy(x => 1).Select(g => new { Recebido = g.Sum(x => x.ValorRecebido), Saldo = g.Sum(x => x.SaldoAberto), ValorVencido = g.Sum(x => x.Status == ContaReceberStatus.Vencido ? x.SaldoAberto : 0), Vencidos = g.Count(x => x.Status == ContaReceberStatus.Vencido) }).SingleOrDefaultAsync(ct);
        var recebimentos = db.Recebimentos.AsNoTracking().Where(x => !x.Estornado);
        if (request.Inicio.HasValue) recebimentos = recebimentos.Where(x => x.DataRecebimento >= request.Inicio);
        if (request.Fim.HasValue) recebimentos = recebimentos.Where(x => x.DataRecebimento <= request.Fim.Value.AddDays(1).AddTicks(-1));
        if (request.ConvenioId.HasValue) recebimentos = recebimentos.Where(x => x.ContaReceber.ConvenioId == request.ConvenioId);
        if (request.PacienteId.HasValue) recebimentos = recebimentos.Where(x => x.ContaReceber.PacienteId == request.PacienteId);
        if (request.MedicoId.HasValue) recebimentos = recebimentos.Where(x => x.ContaReceber.Faturamento.AtendimentoCirurgico.MedicoResponsavelId == request.MedicoId || x.ContaReceber.Faturamento.AtendimentoCirurgico.MedicoAuxiliar1Id == request.MedicoId || x.ContaReceber.Faturamento.AtendimentoCirurgico.MedicoAuxiliar2Id == request.MedicoId);
        var recebidoPeriodo = await recebimentos.SumAsync(x => (decimal?)x.ValorRecebido, ct) ?? 0;
        var monthlyRows = await contas.Where(x => x.Status != ContaReceberStatus.Cancelado).GroupBy(x => x.Competencia)
            .Select(g => new { Competencia = g.Key, Apresentado = g.Sum(x => x.ValorOriginal), Reconhecido = g.Sum(x => x.ValorAjustado), Recebido = g.Sum(x => x.ValorRecebido), Saldo = g.Sum(x => x.SaldoAberto) })
            .OrderBy(x => x.Competencia).ToListAsync(ct);
        var monthly = monthlyRows.Select(x => new FinanceiroResumoMensalDto(x.Competencia, x.Apresentado, x.Reconhecido, x.Recebido, x.Saldo)).ToList();
        return new(f?.Apresentado ?? 0, f?.Glosado ?? 0, f?.Recuperado ?? 0, f?.Reconhecido ?? 0, c?.Recebido ?? 0, c?.Saldo ?? 0, c?.ValorVencido ?? 0, recebidoPeriodo, c?.Vencidos ?? 0, monthly);
    }
}

public sealed class ObterPacienteFinanceiroResumoQueryHandler(IFinanceFeatureDbContext db)
    : IRequestHandler<ObterPacienteFinanceiroResumoQuery, PacienteFinanceiroResumoDto>
{
    public async Task<PacienteFinanceiroResumoDto> Handle(ObterPacienteFinanceiroResumoQuery request, CancellationToken ct)
    {
        var paciente = await db.Pacientes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PacienteId, ct)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");
        if (request.CurrentPerfilId == Perfil.PacientesId && paciente.UserId != request.CurrentUserId)
            throw new UnauthorizedAccessException("Paciente sem acesso a este resumo.");
        if (request.CurrentPerfilId == Perfil.MedicosId && !await db.AtendimentosCirurgicos.AnyAsync(x => x.PacienteId == request.PacienteId
            && (x.MedicoResponsavelId == request.CurrentUserId || x.MedicoAuxiliar1Id == request.CurrentUserId || x.MedicoAuxiliar2Id == request.CurrentUserId), ct))
            throw new UnauthorizedAccessException("Medico sem acesso a este resumo.");
        var f = await db.Faturamentos.AsNoTracking().Where(x => x.AtendimentoCirurgico.PacienteId == request.PacienteId)
            .GroupBy(x => 1).Select(g => new { Apresentado = g.Sum(x => x.ValorApresentado), Glosado = g.Sum(x => x.ValorGlosado), Reconhecido = g.Sum(x => x.ValorReconhecido) }).SingleOrDefaultAsync(ct);
        var contas = await db.ContasReceber.AsNoTracking().Where(x => x.PacienteId == request.PacienteId && x.Status != ContaReceberStatus.Cancelado).ToListAsync(ct);
        if (f == null && contas.Count == 0)
        {
            var avisos = new List<string>();
            var paymentValid = LegacyFinanceiroFallback.TryParseCurrency(paciente.Pagamento, out var presented);
            var glosaValid = LegacyFinanceiroFallback.TryParseCurrency(paciente.RepasseGlosa, out var glosa);
            if (!string.IsNullOrWhiteSpace(paciente.Pagamento) && !paymentValid) avisos.Add("Paciente.Pagamento legado requer conciliacao manual.");
            if (!string.IsNullOrWhiteSpace(paciente.RepasseGlosa) && !glosaValid) avisos.Add("Paciente.RepasseGlosa legado requer conciliacao manual.");
            if (!paymentValid) presented = 0;
            if (!glosaValid) glosa = 0;
            glosa = Math.Min(glosa, presented); var recognized = Math.Max(0, presented - glosa);
            var receivedLegacy = paciente.StatusPago ? recognized : 0; var balanceLegacy = recognized - receivedLegacy;
            var legacyStatus = avisos.Count > 0 ? "Requer conciliacao" : paciente.StatusPago ? "Recebido (legado)"
                : balanceLegacy > 0 ? "Em aberto (legado)" : "Sem movimentacao";
            return new(presented, glosa, recognized, receivedLegacy, balanceLegacy, legacyStatus, "Legado", avisos);
        }
        var recebido = contas.Sum(x => x.ValorRecebido); var saldo = contas.Sum(x => x.SaldoAberto);
        var status = contas.Any(x => x.Status == ContaReceberStatus.Vencido) ? "Vencido"
            : saldo <= 0 && recebido > 0 ? "Recebido" : recebido > 0 ? "Parcialmente recebido"
            : saldo > 0 ? "Em aberto" : "Sem movimentacao";
        var inconsistencias = await db.FinanceiroMigracaoInconsistencias.AsNoTracking()
            .Where(x => x.PacienteId == request.PacienteId && !x.Resolvida).Select(x => x.Motivo).ToListAsync(ct);
        return new(f?.Apresentado ?? 0, f?.Glosado ?? 0, f?.Reconhecido ?? 0, recebido, saldo,
            inconsistencias.Count > 0 ? "Requer conciliacao" : status, "Normalizado", inconsistencias);
    }
}


