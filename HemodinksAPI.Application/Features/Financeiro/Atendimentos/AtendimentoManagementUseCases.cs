using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class ObterAtendimentoQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ObterAtendimentoQuery, AtendimentoDto>
{
    public async Task<AtendimentoDto> Handle(ObterAtendimentoQuery request, CancellationToken ct)
    {
        var query = FinanceiroManagementQueries.FullAtendimento(db.AtendimentosCirurgicos.AsNoTracking());
        if (request.CurrentPerfilId == Perfil.MedicosId)
            query = query.Where(x => x.MedicoResponsavelId == request.CurrentUserId || x.MedicoAuxiliar1Id == request.CurrentUserId || x.MedicoAuxiliar2Id == request.CurrentUserId);
        return FinanceiroMapper.ToDto(await query.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado."));
    }
}

public sealed class AtualizarAtendimentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarAtendimentoCommand, AtendimentoDto>
{
    public async Task<AtendimentoDto> Handle(AtualizarAtendimentoCommand request, CancellationToken ct)
    {
        var query = FinanceiroManagementQueries.FullAtendimento(db.AtendimentosCirurgicos)
            .Include(x => x.Faturamentos).ThenInclude(x => x.Itens)
            .Include(x => x.Faturamentos).ThenInclude(x => x.Glosas).ThenInclude(x => x.Recursos)
            .Include(x => x.Faturamentos).ThenInclude(x => x.ContasReceber).ThenInclude(x => x.Recebimentos)
            .AsQueryable();
        if (request.CurrentPerfilId == Perfil.MedicosId)
        {
            query = query.Where(x => x.MedicoResponsavelId == request.CurrentUserId
                || x.MedicoAuxiliar1Id == request.CurrentUserId
                || x.MedicoAuxiliar2Id == request.CurrentUserId);
        }

        var item = await query.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        var doctors = new[] { request.MedicoResponsavelId, request.MedicoAuxiliar1Id ?? 0, request.MedicoAuxiliar2Id ?? 0 }.Where(x => x > 0).ToArray();
        if (doctors.Distinct().Count() != doctors.Length) throw new InvalidOperationException("Os medicos devem ser distintos.");
        if (request.ValorGlosa < 0 || request.ValorGlosa > 0 && string.IsNullOrWhiteSpace(request.MotivoGlosa))
            throw new InvalidOperationException("Informe um valor de glosa valido e o respectivo motivo.");
        var previousGlosa = item.ValorGlosa;
        var previousMotivoGlosa = item.MotivoGlosa;
        var previousDataProcedimento = item.DataProcedimento;
        item.DataProcedimento = request.DataProcedimento; item.HospitalId = request.HospitalId; item.ConvenioId = request.ConvenioId;
        item.OpmeFornecedorId = request.OpmeFornecedorId;
        item.MedicoResponsavelId = request.MedicoResponsavelId; item.MedicoAuxiliar1Id = request.MedicoAuxiliar1Id;
        item.MedicoAuxiliar2Id = request.MedicoAuxiliar2Id; item.Diagnostico = request.Diagnostico?.Trim();
        item.TratamentoMedico = request.TratamentoMedico?.Trim(); item.NumeroAutorizacao = request.NumeroAutorizacao?.Trim();
        item.ValorGlosa = request.ValorGlosa > 0 ? request.ValorGlosa : null;
        item.MotivoGlosa = request.ValorGlosa > 0 ? request.MotivoGlosa?.Trim() : null;
        item.Observacao = request.Observacao?.Trim();
        item.Status = request.Status; item.DataAtualizacao = DateTime.UtcNow;
        foreach (var faturamento in item.Faturamentos)
        {
            var glosaAtendimento = faturamento.Glosas.FirstOrDefault(glosa =>
                glosa.FaturamentoItemId == null
                && (glosa.Observacao == "Glosa informada no atendimento"
                    || glosa.Observacao == null
                    && glosa.ValorGlosado == previousGlosa
                    && glosa.DescricaoMotivo == previousMotivoGlosa
                    && glosa.DataGlosa == previousDataProcedimento));
            var requestedGlosa = request.ValorGlosa ?? 0m;
            if (requestedGlosa > faturamento.ValorApresentado)
                throw new InvalidOperationException("A glosa informada no atendimento excede o valor apresentado.");
            if (requestedGlosa > 0)
            {
                glosaAtendimento ??= new Glosa
                {
                    ClinicaId = faturamento.ClinicaId,
                    Status = GlosaStatus.Aberta,
                    Observacao = "Glosa informada no atendimento"
                };
                if (glosaAtendimento.Id == 0 && !faturamento.Glosas.Contains(glosaAtendimento))
                    faturamento.Glosas.Add(glosaAtendimento);
                glosaAtendimento.DescricaoMotivo = request.MotivoGlosa!.Trim();
                glosaAtendimento.ValorGlosado = requestedGlosa;
                glosaAtendimento.DataGlosa = request.DataProcedimento;
            }
            else if (glosaAtendimento != null)
            {
                db.Glosas.Remove(glosaAtendimento);
            }
            FinanceiroCalculations.Recalculate(faturamento);
            FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(faturamento, DateTime.UtcNow);
            faturamento.DataAtualizacao = DateTime.UtcNow;
        }
        if (item.Faturamentos.Count == 0 && request.Procedimentos.Count > 0)
        {
            db.AtendimentoProcedimentos.RemoveRange(item.Procedimentos); item.Procedimentos.Clear(); var order = 0;
            foreach (var p in request.Procedimentos)
            {
                if (p.Quantidade <= 0 || p.PesoPercentual < 0 || string.IsNullOrWhiteSpace(p.Descricao) && string.IsNullOrWhiteSpace(p.CbhpmCodigo))
                    throw new InvalidOperationException("Procedimento invalido.");
                var procedimento = await FinanceiroProcedimentoResolver.ResolveAsync(
                    db, p, request.ConvenioId, request.DataProcedimento, ct);
                if (string.IsNullOrWhiteSpace(procedimento.Descricao))
                    throw new InvalidOperationException("Descricao obrigatoria para procedimento sem cadastro CBHPM.");
                item.Procedimentos.Add(new AtendimentoProcedimento
                {
                    ClinicaId = item.ClinicaId,
                    CbhpmCodigo = procedimento.Codigo,
                    CbhpmPorte = procedimento.Porte,
                    Descricao = procedimento.Descricao,
                    Quantidade = p.Quantidade,
                    PesoPercentual = p.PesoPercentual,
                    ValorReferencia = procedimento.ValorReferencia,
                    ValorNegociado = procedimento.ValorNegociado,
                    Ordem = ++order
                });
            }
        }
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(item);
    }
}

public sealed class ExcluirAtendimentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<ExcluirAtendimentoCommand>
{
    public async Task Handle(ExcluirAtendimentoCommand request, CancellationToken ct)
    {
        var query = db.AtendimentosCirurgicos.Include(x => x.Faturamentos).AsQueryable();
        if (request.CurrentPerfilId == Perfil.MedicosId)
        {
            query = query.Where(x => x.MedicoResponsavelId == request.CurrentUserId
                || x.MedicoAuxiliar1Id == request.CurrentUserId
                || x.MedicoAuxiliar2Id == request.CurrentUserId);
        }

        var item = await query.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        if (item.Faturamentos.Count > 0) throw new InvalidOperationException("Atendimento faturado nao pode ser excluido.");
        db.AtendimentosCirurgicos.Remove(item); await db.SaveChangesAsync(ct);
    }
}


