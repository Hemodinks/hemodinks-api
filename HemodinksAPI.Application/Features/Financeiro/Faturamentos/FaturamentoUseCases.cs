using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class CriarFaturamentoCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant)
    : IRequestHandler<CriarFaturamentoCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(CriarFaturamentoCommand request, CancellationToken ct)
    {
        var atendimento = await db.AtendimentosCirurgicos.Include(x => x.Paciente).Include(x => x.Procedimentos)
            .SingleOrDefaultAsync(x => x.Id == request.AtendimentoCirurgicoId, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        if (atendimento.Status == AtendimentoCirurgicoStatus.Cancelado)
            throw new InvalidOperationException("Nao e possivel faturar atendimento cancelado.");
        var faturamento = new Faturamento
        {
            ClinicaId = tenant.GetRequiredClinicaId(), AtendimentoCirurgico = atendimento, ConvenioId = atendimento.ConvenioId,
            NumeroGuia = request.NumeroGuia?.Trim(), NumeroLote = request.NumeroLote?.Trim(),
            Competencia = new DateTime(request.Competencia.Year, request.Competencia.Month, 1), Observacao = request.Observacao?.Trim()
        };
        foreach (var p in atendimento.Procedimentos.OrderBy(x => x.Ordem))
        {
            var unit = p.ValorNegociado ?? p.ValorReferencia ?? 0m;
            var presented = FinanceiroCalculations.CalculatePresentedValue(p.Quantidade, p.PesoPercentual, unit);
            faturamento.Itens.Add(new FaturamentoItem
            {
                ClinicaId = faturamento.ClinicaId, AtendimentoProcedimento = p, Codigo = p.CbhpmCodigo,
                Descricao = p.Descricao, Quantidade = p.Quantidade, PesoPercentual = p.PesoPercentual,
                ValorUnitario = unit, ValorApresentado = presented, ValorAprovado = presented,
                Status = FaturamentoItemStatus.Rascunho, Ordem = p.Ordem
            });
        }
        var requestedGlosa = atendimento.ValorGlosa ?? 0m;
        if (requestedGlosa > 0)
        {
            var presentedTotal = faturamento.Itens.Sum(x => x.ValorApresentado);
            if (requestedGlosa > presentedTotal)
                throw new InvalidOperationException("A glosa informada no atendimento excede o valor apresentado.");
            faturamento.Glosas.Add(new Glosa
            {
                ClinicaId = faturamento.ClinicaId,
                DescricaoMotivo = atendimento.MotivoGlosa!,
                ValorGlosado = requestedGlosa,
                DataGlosa = atendimento.DataProcedimento,
                Status = GlosaStatus.Aberta,
                Observacao = "Glosa informada no atendimento"
            });
        }
        FinanceiroCalculations.Recalculate(faturamento);
        db.Faturamentos.Add(faturamento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(faturamento);
    }
}

public sealed class ListarFaturamentosQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ListarFaturamentosQuery, List<FaturamentoDto>>
{
    public async Task<List<FaturamentoDto>> Handle(ListarFaturamentosQuery request, CancellationToken ct) =>
        (await ApplyScope(Full(db.Faturamentos.AsNoTracking()), request)
            .OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro)
            .ThenByDescending(x => x.Id).ToListAsync(ct)).Select(FinanceiroMapper.ToDto).ToList();
    private static IQueryable<Faturamento> ApplyScope(IQueryable<Faturamento> query, ListarFaturamentosQuery request) =>
        request.CurrentPerfilId == Perfil.MedicosId
            ? query.Where(x => x.AtendimentoCirurgico.MedicoResponsavelId == request.CurrentUserId
                || x.AtendimentoCirurgico.MedicoAuxiliar1Id == request.CurrentUserId
                || x.AtendimentoCirurgico.MedicoAuxiliar2Id == request.CurrentUserId)
            : query;
    internal static IQueryable<Faturamento> Full(IQueryable<Faturamento> query) => query
        .Include(x => x.AtendimentoCirurgico).ThenInclude(x => x.Paciente).Include(x => x.Itens)
        .Include(x => x.Glosas).ThenInclude(x => x.Recursos)
        .Include(x => x.ContasReceber).ThenInclude(x => x.Recebimentos);
}

public sealed class AtualizarStatusFaturamentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarStatusFaturamentoCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(AtualizarStatusFaturamentoCommand request, CancellationToken ct)
    {
        var faturamento = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (!faturamento.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("O faturamento foi alterado por outro usuario.");
        var allowed = faturamento.Status switch
        {
            FaturamentoStatus.Rascunho => request.Status is FaturamentoStatus.ProntoParaEnvio or FaturamentoStatus.Cancelado,
            FaturamentoStatus.ProntoParaEnvio => request.Status is FaturamentoStatus.Rascunho or FaturamentoStatus.Enviado or FaturamentoStatus.Cancelado,
            FaturamentoStatus.Enviado => request.Status is FaturamentoStatus.EmAnalise or FaturamentoStatus.Cancelado,
            _ => request.Status == faturamento.Status
        };
        if (!allowed) throw new InvalidOperationException($"Transicao de {faturamento.Status} para {request.Status} nao permitida.");
        faturamento.Status = request.Status;
        if (request.Status == FaturamentoStatus.Enviado && faturamento.DataEnvio == null) faturamento.DataEnvio = DateTime.UtcNow;
        faturamento.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(faturamento);
    }
}

public sealed class RegistrarGlosaCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(RegistrarGlosaCommand request, CancellationToken ct)
    {
        var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.FaturamentoId, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (request.ValorGlosado <= 0 || f.ValorGlosado + request.ValorGlosado > f.ValorApresentado)
            throw new InvalidOperationException("Valor da glosa excede o valor apresentado disponivel.");
        if (request.FaturamentoItemId.HasValue && f.Itens.All(x => x.Id != request.FaturamentoItemId))
            throw new InvalidOperationException("Item nao pertence ao faturamento.");
        f.Glosas.Add(new Glosa { ClinicaId = tenant.GetRequiredClinicaId(), FaturamentoItemId = request.FaturamentoItemId,
            CodigoMotivo = request.CodigoMotivo?.Trim(), DescricaoMotivo = request.DescricaoMotivo.Trim(),
            ValorGlosado = request.ValorGlosado, DataGlosa = request.DataGlosa, Observacao = request.Observacao?.Trim() });
        FinanceiroCalculations.Recalculate(f);
        FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(f, DateTime.UtcNow);
        f.Status = f.ValorGlosado >= f.ValorApresentado ? FaturamentoStatus.GlosadoTotal : FaturamentoStatus.GlosadoParcial;
        f.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(f);
    }
}

public sealed class RegistrarRetornoFaturamentoCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant)
    : IRequestHandler<RegistrarRetornoFaturamentoCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(RegistrarRetornoFaturamentoCommand request, CancellationToken ct)
    {
        var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (!f.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("O faturamento foi alterado por outro usuario.");
        if (f.Glosas.Any(x => x.Recursos.Count > 0)) throw new InvalidOperationException("Retorno nao pode ser substituido depois de iniciado um recurso de glosa.");
        db.Glosas.RemoveRange(f.Glosas);
        f.Glosas.Clear();
        foreach (var result in request.Itens)
        {
            var item = f.Itens.SingleOrDefault(x => x.Id == result.FaturamentoItemId)
                ?? throw new InvalidOperationException("Item do retorno nao pertence ao faturamento.");
            if (result.ValorGlosado < 0 || result.ValorAprovado < 0
                || result.ValorGlosado + result.ValorAprovado != item.ValorApresentado)
                throw new InvalidOperationException("Aprovado mais glosado deve ser igual ao valor apresentado do item.");
            item.ValorGlosado = result.ValorGlosado; item.ValorAprovado = result.ValorAprovado;
            item.MotivoGlosa = result.MotivoGlosa?.Trim(); item.DataAtualizacao = DateTime.UtcNow;
            item.Status = result.ValorGlosado == 0 ? FaturamentoItemStatus.Aprovado
                : result.ValorAprovado == 0 ? FaturamentoItemStatus.GlosadoTotal : FaturamentoItemStatus.GlosadoParcial;
            if (result.ValorGlosado > 0)
                f.Glosas.Add(new Glosa { ClinicaId = tenant.GetRequiredClinicaId(), FaturamentoItem = item,
                    CodigoMotivo = result.CodigoMotivo?.Trim(), DescricaoMotivo = result.MotivoGlosa?.Trim() ?? "Glosa sem motivo informado",
                    ValorGlosado = result.ValorGlosado, DataGlosa = request.DataRetorno });
        }
        f.DataRetorno = request.DataRetorno; f.DataAtualizacao = DateTime.UtcNow;
        FinanceiroCalculations.Recalculate(f);
        FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(f, DateTime.UtcNow);
        f.Status = f.ValorGlosado == 0 ? FaturamentoStatus.Aprovado
            : f.ValorReconhecido == 0 ? FaturamentoStatus.GlosadoTotal : FaturamentoStatus.GlosadoParcial;
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(f);
    }
}

public sealed class RegistrarRecursoGlosaCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarRecursoGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(RegistrarRecursoGlosaCommand request, CancellationToken ct)
    {
        var g = await db.Glosas.Include(x => x.Recursos).Include(x => x.Faturamento).ThenInclude(x => x.AtendimentoCirurgico).ThenInclude(x => x.Paciente)
            .Include(x => x.Faturamento).ThenInclude(x => x.Itens).Include(x => x.Faturamento).ThenInclude(x => x.Glosas).ThenInclude(x => x.Recursos)
            .Include(x => x.Faturamento).ThenInclude(x => x.ContasReceber).ThenInclude(x => x.Recebimentos)
            .SingleOrDefaultAsync(x => x.Id == request.GlosaId, ct) ?? throw new KeyNotFoundException("Glosa nao encontrada.");
        if (request.ValorRecorrido <= 0 || request.ValorRecorrido > g.ValorGlosado || request.ValorRecuperado < 0 || request.ValorRecuperado > request.ValorRecorrido)
            throw new InvalidOperationException("Valores do recurso sao invalidos.");
        g.Recursos.Add(new RecursoGlosa { ClinicaId = tenant.GetRequiredClinicaId(), DataEnvio = request.DataEnvio,
            Justificativa = request.Justificativa.Trim(), ValorRecorrido = request.ValorRecorrido,
            DataResposta = request.DataResposta, ValorRecuperado = request.ValorRecuperado, Status = request.Status,
            Observacao = request.Observacao?.Trim() });
        g.Status = request.Status switch { RecursoGlosaStatus.Aceito => GlosaStatus.RevertidaTotal,
            RecursoGlosaStatus.AceitoParcialmente => GlosaStatus.RevertidaParcial,
            RecursoGlosaStatus.Enviado => GlosaStatus.EmRecurso, _ => g.Status };
        FinanceiroCalculations.Recalculate(g.Faturamento);
        FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(g.Faturamento, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(g.Faturamento);
    }
}


