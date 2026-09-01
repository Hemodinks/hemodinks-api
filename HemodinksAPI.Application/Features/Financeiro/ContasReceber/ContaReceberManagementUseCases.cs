using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class ObterContaReceberQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ObterContaReceberQuery, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(ObterContaReceberQuery request, CancellationToken ct) => FinanceiroMapper.ToDto(
        await FinanceiroManagementQueries.FullConta(db.ContasReceber.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
        ?? throw new KeyNotFoundException("Conta nao encontrada."));
}

public sealed class AtualizarContaReceberCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<AtualizarContaReceberCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(AtualizarContaReceberCommand request, CancellationToken ct)
    {
        var item = await FinanceiroManagementQueries.FullConta(db.ContasReceber).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Conta nao encontrada.");
        if (!item.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("A conta foi alterada por outro usuario.");
        if (item.Recebimentos.Any(x => !x.Estornado)) throw new InvalidOperationException("Titulo com recebimento ativo nao pode ter valores alterados.");
        if (request.ValorOriginal < 0 || request.ValorAjustado < 0) throw new InvalidOperationException("Valores invalidos.");
        item.NumeroDocumento = request.NumeroDocumento.Trim(); item.Descricao = request.Descricao.Trim(); item.DataEmissao = request.DataEmissao;
        item.DataVencimento = request.DataVencimento; item.ValorOriginal = request.ValorOriginal; item.ValorAjustado = request.ValorAjustado;
        item.Observacao = request.Observacao?.Trim(); item.DataAtualizacao = DateTime.UtcNow;
        FinanceiroCalculations.Recalculate(item, DateTime.UtcNow); await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(item);
    }
}

public sealed class CancelarContaReceberCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<CancelarContaReceberCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(CancelarContaReceberCommand request, CancellationToken ct)
    {
        var item = await FinanceiroManagementQueries.FullConta(db.ContasReceber).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Conta nao encontrada.");
        if (!item.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("A conta foi alterada por outro usuario.");
        if (item.Recebimentos.Any(x => !x.Estornado)) throw new InvalidOperationException("Estorne os recebimentos antes de cancelar o titulo.");
        if (string.IsNullOrWhiteSpace(request.Motivo)) throw new InvalidOperationException("Motivo obrigatorio.");
        item.Status = ContaReceberStatus.Cancelado; item.SaldoAberto = 0; item.Observacao = $"{item.Observacao}\nCancelamento: {request.Motivo.Trim()}".Trim();
        item.DataAtualizacao = DateTime.UtcNow; await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(item);
    }
}

public sealed class ExcluirConvenioProcedimentoPrecoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<ExcluirConvenioProcedimentoPrecoCommand>
{
    public async Task Handle(ExcluirConvenioProcedimentoPrecoCommand request, CancellationToken ct)
    {
        var item = await db.ConvenioProcedimentoPrecos.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Preco nao encontrado.");
        item.Ativo = false; item.DataAtualizacao = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
}

public sealed class PesquisarContasReceberQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<PesquisarContasReceberQuery, PagedResult<ContaReceberDto>>
{
    public async Task<PagedResult<ContaReceberDto>> Handle(PesquisarContasReceberQuery request, CancellationToken ct)
    {
        FinanceiroManagementQueries.ValidatePage(request.Page, request.PageSize);
        var query = FinanceiroManagementQueries.FullConta(db.ContasReceber.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(request.Termo)) { var term = request.Termo.Trim(); query = query.Where(x => x.NumeroDocumento.Contains(term) || x.Paciente.NomePaciente.Contains(term)); }
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status);
        if (request.VencimentoInicio.HasValue) query = query.Where(x => x.DataVencimento >= request.VencimentoInicio);
        if (request.VencimentoFim.HasValue) query = query.Where(x => x.DataVencimento <= request.VencimentoFim);
        if (request.ConvenioId.HasValue) query = query.Where(x => x.ConvenioId == request.ConvenioId);
        if (request.MedicoId.HasValue) query = query.Where(x => x.Faturamento.AtendimentoCirurgico.MedicoResponsavelId == request.MedicoId
            || x.Faturamento.AtendimentoCirurgico.MedicoAuxiliar1Id == request.MedicoId || x.Faturamento.AtendimentoCirurgico.MedicoAuxiliar2Id == request.MedicoId);
        if (request.PacienteId.HasValue) query = query.Where(x => x.PacienteId == request.PacienteId);
        var count = await query.CountAsync(ct); var items = await query
            .OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(FinanceiroMapper.ToDto).ToList(), request.Page, request.PageSize, count);
    }
}

public sealed class PesquisarFaturamentosQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<PesquisarFaturamentosQuery, PagedResult<FaturamentoDto>>
{
    public async Task<PagedResult<FaturamentoDto>> Handle(PesquisarFaturamentosQuery request, CancellationToken ct)
    {
        FinanceiroManagementQueries.ValidatePage(request.Page, request.PageSize); var query = ListarFaturamentosQueryHandler.Full(db.Faturamentos.AsNoTracking());
        if (request.CurrentPerfilId == Perfil.MedicosId) query = query.Where(x => x.AtendimentoCirurgico.MedicoResponsavelId == request.CurrentUserId || x.AtendimentoCirurgico.MedicoAuxiliar1Id == request.CurrentUserId || x.AtendimentoCirurgico.MedicoAuxiliar2Id == request.CurrentUserId);
        if (!string.IsNullOrWhiteSpace(request.Termo)) { var term = request.Termo.Trim(); query = query.Where(x => x.NumeroGuia!.Contains(term) || x.NumeroLote!.Contains(term) || x.AtendimentoCirurgico.Paciente.NomePaciente.Contains(term)); }
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status);
        if (request.CompetenciaInicio.HasValue) query = query.Where(x => x.Competencia >= request.CompetenciaInicio);
        if (request.CompetenciaFim.HasValue) query = query.Where(x => x.Competencia <= request.CompetenciaFim);
        if (request.ConvenioId.HasValue) query = query.Where(x => x.ConvenioId == request.ConvenioId);
        var count = await query.CountAsync(ct); var items = await query
            .OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(FinanceiroMapper.ToDto).ToList(), request.Page, request.PageSize, count);
    }
}


