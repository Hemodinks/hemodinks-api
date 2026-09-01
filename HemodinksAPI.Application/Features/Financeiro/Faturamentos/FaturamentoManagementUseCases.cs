using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class ObterFaturamentoQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ObterFaturamentoQuery, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(ObterFaturamentoQuery request, CancellationToken ct)
    {
        var query = ListarFaturamentosQueryHandler.Full(db.Faturamentos.AsNoTracking());
        if (request.CurrentPerfilId == Perfil.MedicosId)
            query = query.Where(x => x.AtendimentoCirurgico.MedicoResponsavelId == request.CurrentUserId || x.AtendimentoCirurgico.MedicoAuxiliar1Id == request.CurrentUserId || x.AtendimentoCirurgico.MedicoAuxiliar2Id == request.CurrentUserId);
        return FinanceiroMapper.ToDto(await query.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado."));
    }
}

public sealed class AtualizarFaturamentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarFaturamentoCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(AtualizarFaturamentoCommand request, CancellationToken ct)
    {
        var item = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (!item.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("O faturamento foi alterado por outro usuario.");
        if (item.Status != FaturamentoStatus.Rascunho) throw new InvalidOperationException("Somente faturamento em rascunho pode ser editado.");
        item.NumeroGuia = request.NumeroGuia?.Trim(); item.NumeroLote = request.NumeroLote?.Trim();
        item.Competencia = new DateTime(request.Competencia.Year, request.Competencia.Month, 1);
        item.Observacao = request.Observacao?.Trim(); item.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(item);
    }
}

public sealed class ExcluirFaturamentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<ExcluirFaturamentoCommand>
{
    public async Task Handle(ExcluirFaturamentoCommand request, CancellationToken ct)
    {
        var item = await db.Faturamentos.Include(x => x.ContasReceber).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (item.Status != FaturamentoStatus.Rascunho || item.ContasReceber.Count > 0)
            throw new InvalidOperationException("Somente faturamento em rascunho e sem titulos pode ser excluido.");
        db.Faturamentos.Remove(item); await db.SaveChangesAsync(ct);
    }
}

public sealed class AtualizarFaturamentoItemCommandHandler(IFinanceFeatureDbContext db)
    : IRequestHandler<AtualizarFaturamentoItemCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(AtualizarFaturamentoItemCommand request, CancellationToken ct)
    {
        var faturamento = await ListarFaturamentosQueryHandler.Full(db.Faturamentos)
            .SingleOrDefaultAsync(x => x.Id == request.FaturamentoId, ct)
            ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (!faturamento.RowVersion.SequenceEqual(request.RowVersion))
            throw new DbUpdateConcurrencyException("O faturamento foi alterado por outro usuario.");
        if (faturamento.Status != FaturamentoStatus.Rascunho)
            throw new InvalidOperationException("Itens so podem ser editados enquanto o faturamento estiver em rascunho.");
        if (request.Quantidade <= 0 || request.PesoPercentual < 0 || request.ValorUnitario < 0 || string.IsNullOrWhiteSpace(request.Descricao))
            throw new InvalidOperationException("Descricao, quantidade, peso e valor do item sao invalidos.");
        var item = faturamento.Itens.SingleOrDefault(x => x.Id == request.ItemId)
            ?? throw new KeyNotFoundException("Item do faturamento nao encontrado.");
        item.Codigo = request.Codigo?.Trim(); item.Descricao = request.Descricao.Trim();
        item.Quantidade = request.Quantidade; item.PesoPercentual = request.PesoPercentual;
        item.ValorUnitario = request.ValorUnitario;
        item.ValorApresentado = FinanceiroCalculations.CalculatePresentedValue(item.Quantidade, item.PesoPercentual, item.ValorUnitario);
        item.ValorAprovado = item.ValorApresentado; item.ValorGlosado = 0; item.Status = FaturamentoItemStatus.Rascunho;
        faturamento.DataAtualizacao = DateTime.UtcNow; FinanceiroCalculations.Recalculate(faturamento);
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(faturamento);
    }
}

public sealed class AtualizarGlosaCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(AtualizarGlosaCommand request, CancellationToken ct)
    {
        var glosa = await db.Glosas.Include(x => x.Recursos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Glosa nao encontrada.");
        if (glosa.Recursos.Count > 0) throw new InvalidOperationException("Glosa com recurso deve ser ajustada pelo retorno do recurso.");
        if (request.ValorGlosado <= 0) throw new InvalidOperationException("Valor glosado deve ser positivo.");
        glosa.CodigoMotivo = request.CodigoMotivo?.Trim(); glosa.DescricaoMotivo = request.DescricaoMotivo.Trim();
        glosa.ValorGlosado = request.ValorGlosado; glosa.DataGlosa = request.DataGlosa; glosa.Observacao = request.Observacao?.Trim();
        glosa.DataAtualizacao = DateTime.UtcNow; return await Recalculate(glosa.FaturamentoId, ct);
    }
    private async Task<FaturamentoDto> Recalculate(int id, CancellationToken ct)
    {
        var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleAsync(x => x.Id == id, ct);
        FinanceiroCalculations.Recalculate(f); FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(f, DateTime.UtcNow);
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(f);
    }
}

public sealed class ExcluirGlosaCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<ExcluirGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(ExcluirGlosaCommand request, CancellationToken ct)
    {
        var glosa = await db.Glosas.Include(x => x.Recursos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Glosa nao encontrada.");
        if (glosa.Recursos.Count > 0) throw new InvalidOperationException("Glosa com recurso nao pode ser excluida.");
        var faturamentoId = glosa.FaturamentoId; db.Glosas.Remove(glosa); await db.SaveChangesAsync(ct);
        var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleAsync(x => x.Id == faturamentoId, ct);
        FinanceiroCalculations.Recalculate(f); FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(f, DateTime.UtcNow);
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(f);
    }
}

public sealed class AtualizarRecursoGlosaCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarRecursoGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(AtualizarRecursoGlosaCommand request, CancellationToken ct)
    {
        var recurso = await db.RecursosGlosa.Include(x => x.Glosa).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Recurso nao encontrado.");
        if (request.ValorRecorrido <= 0 || request.ValorRecuperado < 0 || request.ValorRecuperado > request.ValorRecorrido)
            throw new InvalidOperationException("Valores do recurso sao invalidos.");
        recurso.DataEnvio = request.DataEnvio; recurso.Justificativa = request.Justificativa.Trim(); recurso.ValorRecorrido = request.ValorRecorrido;
        recurso.DataResposta = request.DataResposta; recurso.ValorRecuperado = request.ValorRecuperado; recurso.Status = request.Status;
        recurso.Observacao = request.Observacao?.Trim(); recurso.DataAtualizacao = DateTime.UtcNow;
        return await SaveAndReturn(recurso.Glosa.FaturamentoId, ct);
    }
    private async Task<FaturamentoDto> SaveAndReturn(int id, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct); var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleAsync(x => x.Id == id, ct);
        FinanceiroCalculations.Recalculate(f); FinanceiroCalculations.ReconcileAccountsWithRecognizedValue(f, DateTime.UtcNow);
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(f);
    }
}

public sealed class ExcluirRecursoGlosaCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<ExcluirRecursoGlosaCommand, FaturamentoDto>
{
    public async Task<FaturamentoDto> Handle(ExcluirRecursoGlosaCommand request, CancellationToken ct)
    {
        var recurso = await db.RecursosGlosa.Include(x => x.Glosa).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Recurso nao encontrado.");
        if (recurso.Status != RecursoGlosaStatus.EmPreparacao) throw new InvalidOperationException("Somente recurso em preparacao pode ser excluido.");
        var id = recurso.Glosa.FaturamentoId; db.RecursosGlosa.Remove(recurso); await db.SaveChangesAsync(ct);
        var f = await ListarFaturamentosQueryHandler.Full(db.Faturamentos).SingleAsync(x => x.Id == id, ct);
        FinanceiroCalculations.Recalculate(f); await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(f);
    }
}


