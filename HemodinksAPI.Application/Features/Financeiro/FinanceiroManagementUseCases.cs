using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
}

public record ObterAtendimentoQuery(int Id, int CurrentUserId, int CurrentPerfilId) : IRequest<AtendimentoDto>;
public record AtualizarAtendimentoCommand(int Id, DateTime DataProcedimento, int? HospitalId, int? ConvenioId,
    int MedicoResponsavelId, int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id, string? Diagnostico,
    string? TratamentoMedico, string? NumeroAutorizacao, AtendimentoCirurgicoStatus Status,
    List<AtendimentoProcedimentoInput> Procedimentos) : IRequest<AtendimentoDto>;
public record ExcluirAtendimentoCommand(int Id) : IRequest;
public record ObterFaturamentoQuery(int Id, int CurrentUserId, int CurrentPerfilId) : IRequest<FaturamentoDto>;
public record AtualizarFaturamentoCommand(int Id, string? NumeroGuia, string? NumeroLote, DateTime Competencia,
    string? Observacao, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record ExcluirFaturamentoCommand(int Id) : IRequest;
public record AtualizarGlosaCommand(int Id, string? CodigoMotivo, string DescricaoMotivo, decimal ValorGlosado,
    DateTime DataGlosa, string? Observacao) : IRequest<FaturamentoDto>;
public record ExcluirGlosaCommand(int Id) : IRequest<FaturamentoDto>;
public record AtualizarRecursoGlosaCommand(int Id, DateTime? DataEnvio, string Justificativa,
    decimal ValorRecorrido, DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status,
    string? Observacao) : IRequest<FaturamentoDto>;
public record ExcluirRecursoGlosaCommand(int Id) : IRequest<FaturamentoDto>;
public record ObterContaReceberQuery(int Id) : IRequest<ContaReceberDto>;
public record AtualizarContaReceberCommand(int Id, string NumeroDocumento, string Descricao, DateTime DataEmissao,
    DateTime DataVencimento, decimal ValorOriginal, decimal ValorAjustado, string? Observacao,
    byte[] RowVersion) : IRequest<ContaReceberDto>;
public record CancelarContaReceberCommand(int Id, string Motivo, byte[] RowVersion) : IRequest<ContaReceberDto>;
public record ExcluirConvenioProcedimentoPrecoCommand(int Id) : IRequest;
public record PesquisarContasReceberQuery(int Page = 1, int PageSize = 25, string? Termo = null,
    ContaReceberStatus? Status = null, DateTime? VencimentoInicio = null, DateTime? VencimentoFim = null,
    int? ConvenioId = null) : IRequest<PagedResult<ContaReceberDto>>;
public record PesquisarFaturamentosQuery(int Page, int PageSize, string? Termo, FaturamentoStatus? Status,
    DateTime? CompetenciaInicio, DateTime? CompetenciaFim, int? ConvenioId, int CurrentUserId,
    int CurrentPerfilId) : IRequest<PagedResult<FaturamentoDto>>;
public record FinanceiroResumoDto(decimal ValorApresentado, decimal ValorGlosado, decimal ValorRecuperado,
    decimal ValorReconhecido, decimal ValorRecebido, decimal SaldoAberto, int TitulosVencidos,
    IReadOnlyList<FinanceiroResumoMensalDto> PorCompetencia);
public record FinanceiroResumoMensalDto(DateTime Competencia, decimal Apresentado, decimal Reconhecido,
    decimal Recebido, decimal SaldoAberto);
public record ObterFinanceiroResumoQuery(DateTime? Inicio, DateTime? Fim, int? ConvenioId) : IRequest<FinanceiroResumoDto>;

internal static class FinanceiroManagementQueries
{
    public static IQueryable<AtendimentoCirurgico> FullAtendimento(IQueryable<AtendimentoCirurgico> query) =>
        query.Include(x => x.Paciente).Include(x => x.Procedimentos);

    public static IQueryable<ContaReceber> FullConta(IQueryable<ContaReceber> query) =>
        query.Include(x => x.Paciente).Include(x => x.Recebimentos);

    public static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new InvalidOperationException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
    }
}

public sealed class ObterAtendimentoQueryHandler(IAppDbContext db) : IRequestHandler<ObterAtendimentoQuery, AtendimentoDto>
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

public sealed class AtualizarAtendimentoCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarAtendimentoCommand, AtendimentoDto>
{
    public async Task<AtendimentoDto> Handle(AtualizarAtendimentoCommand request, CancellationToken ct)
    {
        var item = await FinanceiroManagementQueries.FullAtendimento(db.AtendimentosCirurgicos)
            .Include(x => x.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        if (item.Faturamentos.Count > 0 && request.Procedimentos.Count > 0)
            throw new InvalidOperationException("Procedimentos nao podem ser substituidos depois da criacao do faturamento.");
        var doctors = new[] { request.MedicoResponsavelId, request.MedicoAuxiliar1Id ?? 0, request.MedicoAuxiliar2Id ?? 0 }.Where(x => x > 0).ToArray();
        if (doctors.Distinct().Count() != doctors.Length) throw new InvalidOperationException("Os medicos devem ser distintos.");
        item.DataProcedimento = request.DataProcedimento; item.HospitalId = request.HospitalId; item.ConvenioId = request.ConvenioId;
        item.MedicoResponsavelId = request.MedicoResponsavelId; item.MedicoAuxiliar1Id = request.MedicoAuxiliar1Id;
        item.MedicoAuxiliar2Id = request.MedicoAuxiliar2Id; item.Diagnostico = request.Diagnostico?.Trim();
        item.TratamentoMedico = request.TratamentoMedico?.Trim(); item.NumeroAutorizacao = request.NumeroAutorizacao?.Trim();
        item.Status = request.Status; item.DataAtualizacao = DateTime.UtcNow;
        if (item.Faturamentos.Count == 0 && request.Procedimentos.Count > 0)
        {
            db.AtendimentoProcedimentos.RemoveRange(item.Procedimentos); item.Procedimentos.Clear(); var order = 0;
            foreach (var p in request.Procedimentos)
            {
                if (p.Quantidade <= 0 || p.PesoPercentual < 0 || string.IsNullOrWhiteSpace(p.Descricao) && string.IsNullOrWhiteSpace(p.CbhpmCodigo))
                    throw new InvalidOperationException("Procedimento invalido.");
                item.Procedimentos.Add(new AtendimentoProcedimento { ClinicaId = item.ClinicaId, CbhpmCodigo = p.CbhpmCodigo?.Trim(),
                    Descricao = p.Descricao?.Trim() ?? p.CbhpmCodigo!.Trim(), Quantidade = p.Quantidade,
                    PesoPercentual = p.PesoPercentual, Ordem = ++order });
            }
        }
        await db.SaveChangesAsync(ct); return FinanceiroMapper.ToDto(item);
    }
}

public sealed class ExcluirAtendimentoCommandHandler(IAppDbContext db) : IRequestHandler<ExcluirAtendimentoCommand>
{
    public async Task Handle(ExcluirAtendimentoCommand request, CancellationToken ct)
    {
        var item = await db.AtendimentosCirurgicos.Include(x => x.Faturamentos).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        if (item.Faturamentos.Count > 0) throw new InvalidOperationException("Atendimento faturado nao pode ser excluido.");
        db.AtendimentosCirurgicos.Remove(item); await db.SaveChangesAsync(ct);
    }
}

public sealed class ObterFaturamentoQueryHandler(IAppDbContext db) : IRequestHandler<ObterFaturamentoQuery, FaturamentoDto>
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

public sealed class AtualizarFaturamentoCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarFaturamentoCommand, FaturamentoDto>
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

public sealed class ExcluirFaturamentoCommandHandler(IAppDbContext db) : IRequestHandler<ExcluirFaturamentoCommand>
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

public sealed class AtualizarGlosaCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarGlosaCommand, FaturamentoDto>
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

public sealed class ExcluirGlosaCommandHandler(IAppDbContext db) : IRequestHandler<ExcluirGlosaCommand, FaturamentoDto>
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

public sealed class AtualizarRecursoGlosaCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarRecursoGlosaCommand, FaturamentoDto>
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

public sealed class ExcluirRecursoGlosaCommandHandler(IAppDbContext db) : IRequestHandler<ExcluirRecursoGlosaCommand, FaturamentoDto>
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

public sealed class ObterContaReceberQueryHandler(IAppDbContext db) : IRequestHandler<ObterContaReceberQuery, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(ObterContaReceberQuery request, CancellationToken ct) => FinanceiroMapper.ToDto(
        await FinanceiroManagementQueries.FullConta(db.ContasReceber.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == request.Id, ct)
        ?? throw new KeyNotFoundException("Conta nao encontrada."));
}

public sealed class AtualizarContaReceberCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarContaReceberCommand, ContaReceberDto>
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

public sealed class CancelarContaReceberCommandHandler(IAppDbContext db) : IRequestHandler<CancelarContaReceberCommand, ContaReceberDto>
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

public sealed class ExcluirConvenioProcedimentoPrecoCommandHandler(IAppDbContext db) : IRequestHandler<ExcluirConvenioProcedimentoPrecoCommand>
{
    public async Task Handle(ExcluirConvenioProcedimentoPrecoCommand request, CancellationToken ct)
    {
        var item = await db.ConvenioProcedimentoPrecos.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException("Preco nao encontrado.");
        item.Ativo = false; item.DataAtualizacao = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
}

public sealed class PesquisarContasReceberQueryHandler(IAppDbContext db) : IRequestHandler<PesquisarContasReceberQuery, PagedResult<ContaReceberDto>>
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
        var count = await query.CountAsync(ct); var items = await query.OrderBy(x => x.DataVencimento).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(FinanceiroMapper.ToDto).ToList(), request.Page, request.PageSize, count);
    }
}

public sealed class PesquisarFaturamentosQueryHandler(IAppDbContext db) : IRequestHandler<PesquisarFaturamentosQuery, PagedResult<FaturamentoDto>>
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
        var count = await query.CountAsync(ct); var items = await query.OrderByDescending(x => x.Competencia).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(FinanceiroMapper.ToDto).ToList(), request.Page, request.PageSize, count);
    }
}

public sealed class ObterFinanceiroResumoQueryHandler(IAppDbContext db) : IRequestHandler<ObterFinanceiroResumoQuery, FinanceiroResumoDto>
{
    public async Task<FinanceiroResumoDto> Handle(ObterFinanceiroResumoQuery request, CancellationToken ct)
    {
        var faturamentos = db.Faturamentos.AsNoTracking().AsQueryable(); var contas = db.ContasReceber.AsNoTracking().AsQueryable();
        if (request.Inicio.HasValue) { faturamentos = faturamentos.Where(x => x.Competencia >= request.Inicio); contas = contas.Where(x => x.Competencia >= request.Inicio); }
        if (request.Fim.HasValue) { faturamentos = faturamentos.Where(x => x.Competencia <= request.Fim); contas = contas.Where(x => x.Competencia <= request.Fim); }
        if (request.ConvenioId.HasValue) { faturamentos = faturamentos.Where(x => x.ConvenioId == request.ConvenioId); contas = contas.Where(x => x.ConvenioId == request.ConvenioId); }
        var f = await faturamentos.GroupBy(x => 1).Select(g => new { Apresentado = g.Sum(x => x.ValorApresentado), Glosado = g.Sum(x => x.ValorGlosado), Recuperado = g.Sum(x => x.ValorGlosaRecuperada), Reconhecido = g.Sum(x => x.ValorReconhecido) }).SingleOrDefaultAsync(ct);
        var c = await contas.Where(x => x.Status != ContaReceberStatus.Cancelado).GroupBy(x => 1).Select(g => new { Recebido = g.Sum(x => x.ValorRecebido), Saldo = g.Sum(x => x.SaldoAberto), Vencidos = g.Count(x => x.Status == ContaReceberStatus.Vencido) }).SingleOrDefaultAsync(ct);
        var monthlyRows = await contas.Where(x => x.Status != ContaReceberStatus.Cancelado).GroupBy(x => x.Competencia)
            .Select(g => new { Competencia = g.Key, Apresentado = g.Sum(x => x.ValorOriginal), Reconhecido = g.Sum(x => x.ValorAjustado), Recebido = g.Sum(x => x.ValorRecebido), Saldo = g.Sum(x => x.SaldoAberto) })
            .OrderBy(x => x.Competencia).ToListAsync(ct);
        var monthly = monthlyRows.Select(x => new FinanceiroResumoMensalDto(x.Competencia, x.Apresentado, x.Reconhecido, x.Recebido, x.Saldo)).ToList();
        return new(f?.Apresentado ?? 0, f?.Glosado ?? 0, f?.Recuperado ?? 0, f?.Reconhecido ?? 0, c?.Recebido ?? 0, c?.Saldo ?? 0, c?.Vencidos ?? 0, monthly);
    }
}
