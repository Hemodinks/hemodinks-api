using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public record AtendimentoProcedimentoInput(string? CbhpmCodigo, string? Descricao, decimal Quantidade = 1m,
    decimal PesoPercentual = 100m, string? CbhpmPorte = null);
public record CriarAtendimentoCommand(
    int PacienteId, DateTime DataProcedimento, int? HospitalId, int? ConvenioId, int? OpmeFornecedorId,
    string? Hospital, string? Convenio, string? OpmeFornecedor,
    int MedicoResponsavelId, int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id,
    string? Diagnostico, string? TratamentoMedico, string? NumeroAutorizacao,
    AtendimentoCirurgicoStatus Status, List<AtendimentoProcedimentoInput> Procedimentos) : IRequest<AtendimentoDto>
{
    public int CurrentUserId { get; init; }
    public int CurrentPerfilId { get; init; }
}

public record CriarFaturamentoCommand(
    int AtendimentoCirurgicoId, string? NumeroGuia, string? NumeroLote,
    DateTime Competencia, string? Observacao) : IRequest<FaturamentoDto>;
public record AtualizarStatusFaturamentoCommand(int Id, FaturamentoStatus Status, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record RetornoFaturamentoItemInput(int FaturamentoItemId, decimal ValorGlosado, decimal ValorAprovado,
    string? CodigoMotivo, string? MotivoGlosa);
public record RegistrarRetornoFaturamentoCommand(int Id, DateTime DataRetorno,
    List<RetornoFaturamentoItemInput> Itens, byte[] RowVersion) : IRequest<FaturamentoDto>;
public record RegistrarGlosaCommand(int FaturamentoId, int? FaturamentoItemId, string? CodigoMotivo,
    string DescricaoMotivo, decimal ValorGlosado, DateTime DataGlosa, string? Observacao) : IRequest<FaturamentoDto>;
public record RegistrarRecursoGlosaCommand(int GlosaId, DateTime? DataEnvio, string Justificativa,
    decimal ValorRecorrido, DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status, string? Observacao) : IRequest<FaturamentoDto>;
public record GerarContaReceberCommand(int FaturamentoId, string NumeroDocumento, string Descricao,
    DateTime DataEmissao, DateTime DataVencimento, decimal? ValorOriginal, decimal? ValorAjustado, string? Observacao) : IRequest<ContaReceberDto>;
public record RegistrarRecebimentoCommand(int ContaReceberId, DateTime DataRecebimento, decimal ValorRecebido,
    FormaRecebimento FormaRecebimento, string? ReferenciaBancaria, string? DocumentoComprovante,
    string? Observacao, int UsuarioCadastroId, byte[] RowVersion) : IRequest<ContaReceberDto>;
public record EstornarRecebimentoCommand(int RecebimentoId, string MotivoEstorno, int UsuarioEstornoId) : IRequest<ContaReceberDto>;
public record ListarAtendimentosQuery(int? PacienteId = null, int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest<List<AtendimentoDto>>;
public record ListarFaturamentosQuery(int CurrentUserId = 0, int CurrentPerfilId = 0) : IRequest<List<FaturamentoDto>>;
public record ListarContasReceberQuery() : IRequest<List<ContaReceberDto>>;
public record SalvarConvenioProcedimentoPrecoCommand(int? Id, int ConvenioId, string CbhpmCodigo,
    decimal ValorNegociado, decimal PercentualPrincipal, decimal PercentualAuxiliar1, decimal PercentualAuxiliar2,
    DateTime VigenciaInicio, DateTime? VigenciaFinal, bool Ativo) : IRequest<ConvenioProcedimentoPrecoDto>;
public record ListarConvenioProcedimentoPrecosQuery(int? ConvenioId = null, string? CbhpmCodigo = null)
    : IRequest<List<ConvenioProcedimentoPrecoDto>>;

public record AtendimentoProcedimentoDto(int Id, string? CbhpmCodigo, string? CbhpmPorte, string Descricao,
    decimal Quantidade, decimal PesoPercentual, decimal? ValorReferencia, decimal? ValorNegociado, int Ordem);
public record AtendimentoDto(int Id, int PacienteId, string Paciente, DateTime DataProcedimento, int? HospitalId,
    int? ConvenioId, int? OpmeFornecedorId, string? OpmeFornecedor, int MedicoResponsavelId,
    int? MedicoAuxiliar1Id, int? MedicoAuxiliar2Id,
    string? Diagnostico, string? TratamentoMedico, string? NumeroAutorizacao,
    AtendimentoCirurgicoStatus Status, List<AtendimentoProcedimentoDto> Procedimentos);
public record FaturamentoItemDto(int Id, int? AtendimentoProcedimentoId, string? Codigo, string Descricao,
    decimal Quantidade, decimal PesoPercentual, decimal ValorUnitario, decimal ValorApresentado,
    decimal ValorGlosado, decimal ValorAprovado, FaturamentoItemStatus Status, int Ordem);
public record RecursoGlosaDto(int Id, DateTime? DataEnvio, string Justificativa, decimal ValorRecorrido,
    DateTime? DataResposta, decimal ValorRecuperado, RecursoGlosaStatus Status, string? Observacao);
public record GlosaDto(int Id, int? FaturamentoItemId, string? CodigoMotivo, string DescricaoMotivo,
    decimal ValorGlosado, DateTime DataGlosa, GlosaStatus Status, string? Observacao,
    List<RecursoGlosaDto> Recursos);
public record FaturamentoDto(int Id, int AtendimentoCirurgicoId, int PacienteId, string Paciente, int? ConvenioId,
    string? NumeroGuia, string? NumeroLote, DateTime Competencia, DateTime? DataEnvio, DateTime? DataRetorno,
    decimal ValorApresentado, decimal ValorGlosado, decimal ValorGlosaRecuperada, decimal ValorReconhecido,
    FaturamentoStatus Status, string? Observacao, byte[] RowVersion, List<FaturamentoItemDto> Itens, List<GlosaDto> Glosas);
public record RecebimentoDto(int Id, DateTime DataRecebimento, decimal ValorRecebido,
    FormaRecebimento FormaRecebimento, string? ReferenciaBancaria, string? DocumentoComprovante,
    bool Estornado, DateTime? DataEstorno, string? MotivoEstorno);
public record ContaReceberDto(int Id, int FaturamentoId, int PacienteId, string Paciente, int? ConvenioId,
    string NumeroDocumento, string Descricao, DateTime Competencia, DateTime DataEmissao, DateTime DataVencimento,
    decimal ValorOriginal, decimal ValorAjustado, decimal ValorRecebido, decimal SaldoAberto,
    ContaReceberStatus Status, string? Observacao, byte[] RowVersion, List<RecebimentoDto> Recebimentos);
public record ConvenioProcedimentoPrecoDto(int Id, int ConvenioId, string CbhpmCodigo, decimal ValorNegociado,
    decimal PercentualPrincipal, decimal PercentualAuxiliar1, decimal PercentualAuxiliar2,
    DateTime VigenciaInicio, DateTime? VigenciaFinal, bool Ativo);

internal static class FinanceiroMapper
{
    public static AtendimentoDto ToDto(AtendimentoCirurgico x) => new(x.Id, x.PacienteId, x.Paciente.NomePaciente,
        x.DataProcedimento, x.HospitalId, x.ConvenioId, x.OpmeFornecedorId, x.OpmeFornecedor?.Fornecedor,
        x.MedicoResponsavelId, x.MedicoAuxiliar1Id,
        x.MedicoAuxiliar2Id, x.Diagnostico, x.TratamentoMedico, x.NumeroAutorizacao, x.Status,
        x.Procedimentos.OrderBy(p => p.Ordem).Select(p => new AtendimentoProcedimentoDto(p.Id, p.CbhpmCodigo,
            p.CbhpmPorte, p.Descricao, p.Quantidade, p.PesoPercentual, p.ValorReferencia, p.ValorNegociado, p.Ordem)).ToList());

    public static FaturamentoDto ToDto(Faturamento x) => new(x.Id, x.AtendimentoCirurgicoId,
        x.AtendimentoCirurgico.PacienteId, x.AtendimentoCirurgico.Paciente.NomePaciente, x.ConvenioId,
        x.NumeroGuia, x.NumeroLote, x.Competencia, x.DataEnvio, x.DataRetorno, x.ValorApresentado,
        x.ValorGlosado, x.ValorGlosaRecuperada, x.ValorReconhecido, x.Status, x.Observacao, x.RowVersion,
        x.Itens.OrderBy(i => i.Ordem).Select(i => new FaturamentoItemDto(i.Id, i.AtendimentoProcedimentoId,
            i.Codigo, i.Descricao, i.Quantidade, i.PesoPercentual, i.ValorUnitario, i.ValorApresentado,
            i.ValorGlosado, i.ValorAprovado, i.Status, i.Ordem)).ToList(),
        x.Glosas.Select(g => new GlosaDto(g.Id, g.FaturamentoItemId, g.CodigoMotivo, g.DescricaoMotivo,
            g.ValorGlosado, g.DataGlosa, g.Status, g.Observacao, g.Recursos.OrderByDescending(r => r.DataCadastro)
                .Select(r => new RecursoGlosaDto(r.Id, r.DataEnvio, r.Justificativa, r.ValorRecorrido,
                    r.DataResposta, r.ValorRecuperado, r.Status, r.Observacao)).ToList())).ToList());

    public static ContaReceberDto ToDto(ContaReceber x) => new(x.Id, x.FaturamentoId, x.PacienteId,
        x.Paciente.NomePaciente, x.ConvenioId, x.NumeroDocumento, x.Descricao, x.Competencia, x.DataEmissao,
        x.DataVencimento, x.ValorOriginal, x.ValorAjustado, x.ValorRecebido, x.SaldoAberto, x.Status,
        x.Observacao, x.RowVersion, x.Recebimentos.OrderByDescending(r => r.DataRecebimento)
            .Select(r => new RecebimentoDto(r.Id, r.DataRecebimento, r.ValorRecebido, r.FormaRecebimento,
                r.ReferenciaBancaria, r.DocumentoComprovante, r.Estornado, r.DataEstorno, r.MotivoEstorno)).ToList());
}

public sealed class CriarAtendimentoCommandHandler(IAppDbContext db, IClinicaContext tenant)
    : IRequestHandler<CriarAtendimentoCommand, AtendimentoDto>
{
    public async Task<AtendimentoDto> Handle(CriarAtendimentoCommand request, CancellationToken ct)
    {
        var clinicaId = tenant.GetRequiredClinicaId();
        if (request.DataProcedimento == default || request.Procedimentos.Count == 0)
            throw new InvalidOperationException("Data e ao menos um procedimento sao obrigatorios.");
        var medicoResponsavelId = request.CurrentPerfilId == Perfil.MedicosId ? request.CurrentUserId : request.MedicoResponsavelId;
        var ids = new[] { medicoResponsavelId, request.MedicoAuxiliar1Id ?? 0, request.MedicoAuxiliar2Id ?? 0 }.Where(x => x > 0).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("Os medicos do atendimento devem ser distintos.");
        var paciente = await db.Pacientes.SingleOrDefaultAsync(x => x.Id == request.PacienteId, ct)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");
        var hospital = request.HospitalId.HasValue || !string.IsNullOrWhiteSpace(request.Hospital)
            ? await PacienteRules.ResolveHospitalAsync(db, request.HospitalId, request.Hospital, ct)
            : null;
        var convenio = await PacienteRules.ResolveConvenioAsync(db, request.ConvenioId, request.Convenio, ct);
        var opmeFornecedor = await PacienteRules.ResolveOpmeFornecedorAsync(
            db, request.OpmeFornecedorId, request.OpmeFornecedor, ct);
        if (hospital != null)
            hospital.Referencia.ClinicaId = clinicaId;
        if (convenio != null)
            convenio.Referencia.ClinicaId = clinicaId;
        if (opmeFornecedor != null)
            opmeFornecedor.FornecedorReferencia.ClinicaId = clinicaId;
        if (await db.Users.CountAsync(x => ids.Contains(x.Id) && x.PerfilId == Perfil.MedicosId && x.Ativo, ct) != ids.Count)
            throw new InvalidOperationException("Selecione apenas medicos ativos da clinica.");

        var atendimento = new AtendimentoCirurgico
        {
            ClinicaId = clinicaId, Paciente = paciente, DataProcedimento = request.DataProcedimento,
            HospitalId = hospital?.Id > 0 ? hospital.Id : null, Hospital = hospital?.Referencia,
            ConvenioId = convenio?.Id > 0 ? convenio.Id : null, Convenio = convenio?.Referencia,
            OpmeFornecedorId = opmeFornecedor?.Id > 0 ? opmeFornecedor.Id : null,
            OpmeFornecedor = opmeFornecedor?.FornecedorReferencia,
            MedicoResponsavelId = medicoResponsavelId, MedicoAuxiliar1Id = request.MedicoAuxiliar1Id,
            MedicoAuxiliar2Id = request.MedicoAuxiliar2Id, Diagnostico = request.Diagnostico?.Trim(),
            TratamentoMedico = request.TratamentoMedico?.Trim(), NumeroAutorizacao = request.NumeroAutorizacao?.Trim(),
            Status = request.Status
        };

        var order = 0;
        foreach (var input in request.Procedimentos)
        {
            if (input.Quantidade <= 0 || input.PesoPercentual < 0)
                throw new InvalidOperationException("Quantidade e peso do procedimento sao invalidos.");
            var code = input.CbhpmCodigo?.Trim();
            var reference = code == null ? null : await db.CbhpmGeral.AsNoTracking().SingleOrDefaultAsync(x => x.Codigo == code, ct);
            int? convenioId = convenio?.Id > 0 ? convenio.Id : null;
            var negotiated = code == null || convenioId == null ? null : await db.ConvenioProcedimentoPrecos.AsNoTracking()
                .Where(x => x.ConvenioId == convenioId && x.CbhpmCodigo == code && x.Ativo
                    && x.VigenciaInicio <= request.DataProcedimento
                    && (x.VigenciaFinal == null || x.VigenciaFinal >= request.DataProcedimento))
                .OrderByDescending(x => x.VigenciaInicio).FirstOrDefaultAsync(ct);
            var description = reference?.Procedimento ?? input.Descricao?.Trim();
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException("Descricao obrigatoria para procedimento sem cadastro CBHPM.");
            atendimento.Procedimentos.Add(new AtendimentoProcedimento
            {
                ClinicaId = clinicaId, CbhpmCodigo = reference?.Codigo ?? code,
                CbhpmPorte = reference?.Porte ?? input.CbhpmPorte?.Trim().ToUpperInvariant(),
                Descricao = description, Quantidade = input.Quantidade, PesoPercentual = input.PesoPercentual,
                ValorReferencia = reference?.ValorReferencia, ValorNegociado = negotiated?.ValorNegociado,
                Ordem = ++order
            });
        }
        db.AtendimentosCirurgicos.Add(atendimento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(atendimento);
    }
}

public sealed class ListarAtendimentosQueryHandler(IAppDbContext db) : IRequestHandler<ListarAtendimentosQuery, List<AtendimentoDto>>
{
    public async Task<List<AtendimentoDto>> Handle(ListarAtendimentosQuery request, CancellationToken ct)
    {
        var query = db.AtendimentosCirurgicos.AsNoTracking().Include(x => x.Paciente)
            .Include(x => x.OpmeFornecedor).Include(x => x.Procedimentos).AsQueryable();
        if (request.CurrentPerfilId == Perfil.MedicosId)
            query = query.Where(x => x.MedicoResponsavelId == request.CurrentUserId || x.MedicoAuxiliar1Id == request.CurrentUserId || x.MedicoAuxiliar2Id == request.CurrentUserId);
        if (request.PacienteId.HasValue) query = query.Where(x => x.PacienteId == request.PacienteId);
        return (await query.OrderByDescending(x => x.DataProcedimento).ToListAsync(ct)).Select(FinanceiroMapper.ToDto).ToList();
    }
}

public sealed class CriarFaturamentoCommandHandler(IAppDbContext db, IClinicaContext tenant)
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
        FinanceiroCalculations.Recalculate(faturamento);
        db.Faturamentos.Add(faturamento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(faturamento);
    }
}

public sealed class ListarFaturamentosQueryHandler(IAppDbContext db) : IRequestHandler<ListarFaturamentosQuery, List<FaturamentoDto>>
{
    public async Task<List<FaturamentoDto>> Handle(ListarFaturamentosQuery request, CancellationToken ct) =>
        (await ApplyScope(Full(db.Faturamentos.AsNoTracking()), request).OrderByDescending(x => x.Competencia).ToListAsync(ct)).Select(FinanceiroMapper.ToDto).ToList();
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

public sealed class AtualizarStatusFaturamentoCommandHandler(IAppDbContext db) : IRequestHandler<AtualizarStatusFaturamentoCommand, FaturamentoDto>
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

public sealed class RegistrarGlosaCommandHandler(IAppDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarGlosaCommand, FaturamentoDto>
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

public sealed class RegistrarRetornoFaturamentoCommandHandler(IAppDbContext db, IClinicaContext tenant)
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

public sealed class RegistrarRecursoGlosaCommandHandler(IAppDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarRecursoGlosaCommand, FaturamentoDto>
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

public sealed class GerarContaReceberCommandHandler(IAppDbContext db, IClinicaContext tenant) : IRequestHandler<GerarContaReceberCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(GerarContaReceberCommand request, CancellationToken ct)
    {
        var document = request.NumeroDocumento.Trim();
        var existing = await db.ContasReceber.Include(x => x.Paciente).Include(x => x.Recebimentos)
            .SingleOrDefaultAsync(x => x.FaturamentoId == request.FaturamentoId && x.NumeroDocumento == document, ct);
        if (existing != null) return FinanceiroMapper.ToDto(existing);
        var f = await db.Faturamentos.Include(x => x.AtendimentoCirurgico).ThenInclude(x => x.Paciente)
            .SingleOrDefaultAsync(x => x.Id == request.FaturamentoId, ct) ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (f.Status is FaturamentoStatus.Rascunho or FaturamentoStatus.Cancelado)
            throw new InvalidOperationException("O faturamento precisa estar pronto para gerar conta.");
        var original = request.ValorOriginal ?? f.ValorApresentado;
        var adjusted = request.ValorAjustado ?? (f.DataRetorno.HasValue ? f.ValorReconhecido : original);
        if (original < 0 || adjusted < 0) throw new InvalidOperationException("Valores da conta sao invalidos.");
        var existingTotals = await db.ContasReceber.Where(x => x.FaturamentoId == f.Id && x.Status != ContaReceberStatus.Cancelado)
            .Select(x => new { x.ValorOriginal, x.ValorAjustado }).ToListAsync(ct);
        if (existingTotals.Sum(x => x.ValorOriginal) + original > f.ValorApresentado
            || existingTotals.Sum(x => x.ValorAjustado) + adjusted > (f.DataRetorno.HasValue ? f.ValorReconhecido : f.ValorApresentado))
            throw new InvalidOperationException("A soma dos titulos excede o valor disponivel do faturamento.");
        var account = new ContaReceber { ClinicaId = tenant.GetRequiredClinicaId(), Faturamento = f,
            ConvenioId = f.ConvenioId, Paciente = f.AtendimentoCirurgico.Paciente, NumeroDocumento = document,
            Descricao = request.Descricao.Trim(), Competencia = f.Competencia, DataEmissao = request.DataEmissao,
            DataVencimento = request.DataVencimento, ValorOriginal = original, ValorAjustado = adjusted,
            SaldoAberto = adjusted, Observacao = request.Observacao?.Trim() };
        FinanceiroCalculations.Recalculate(account, DateTime.UtcNow);
        db.ContasReceber.Add(account);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(account);
    }
}

public sealed class ListarContasReceberQueryHandler(IAppDbContext db) : IRequestHandler<ListarContasReceberQuery, List<ContaReceberDto>>
{
    public async Task<List<ContaReceberDto>> Handle(ListarContasReceberQuery request, CancellationToken ct)
    {
        var accounts = await db.ContasReceber.Include(x => x.Paciente).Include(x => x.Recebimentos).OrderBy(x => x.DataVencimento).ToListAsync(ct);
        var changed = false;
        foreach (var account in accounts)
        {
            var old = account.Status; FinanceiroCalculations.Recalculate(account, DateTime.UtcNow); changed |= old != account.Status;
        }
        if (changed) await db.SaveChangesAsync(ct);
        return accounts.Select(FinanceiroMapper.ToDto).ToList();
    }
}

public sealed class RegistrarRecebimentoCommandHandler(IAppDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarRecebimentoCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(RegistrarRecebimentoCommand request, CancellationToken ct)
    {
        var account = await db.ContasReceber.Include(x => x.Paciente).Include(x => x.Recebimentos)
            .Include(x => x.Faturamento).ThenInclude(x => x.ContasReceber).ThenInclude(x => x.Recebimentos)
            .SingleOrDefaultAsync(x => x.Id == request.ContaReceberId, ct) ?? throw new KeyNotFoundException("Conta nao encontrada.");
        if (!account.RowVersion.SequenceEqual(request.RowVersion)) throw new DbUpdateConcurrencyException("A conta foi alterada por outro usuario.");
        FinanceiroCalculations.Recalculate(account, DateTime.UtcNow);
        if (account.Status == ContaReceberStatus.Cancelado || request.ValorRecebido <= 0 || request.ValorRecebido > account.SaldoAberto)
            throw new InvalidOperationException("Recebimento invalido ou superior ao saldo aberto.");
        account.Recebimentos.Add(new Recebimento { ClinicaId = tenant.GetRequiredClinicaId(), DataRecebimento = request.DataRecebimento,
            ValorRecebido = request.ValorRecebido, FormaRecebimento = request.FormaRecebimento,
            ReferenciaBancaria = request.ReferenciaBancaria?.Trim(), DocumentoComprovante = request.DocumentoComprovante?.Trim(),
            Observacao = request.Observacao?.Trim(), UsuarioCadastroId = request.UsuarioCadastroId });
        FinanceiroCalculations.Recalculate(account, DateTime.UtcNow); account.DataAtualizacao = DateTime.UtcNow;
        FinanceiroCalculations.RecalculatePaymentStatus(account.Faturamento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(account);
    }
}

public sealed class EstornarRecebimentoCommandHandler(IAppDbContext db) : IRequestHandler<EstornarRecebimentoCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(EstornarRecebimentoCommand request, CancellationToken ct)
    {
        var receipt = await db.Recebimentos.Include(x => x.ContaReceber).ThenInclude(x => x.Paciente)
            .Include(x => x.ContaReceber).ThenInclude(x => x.Recebimentos).SingleOrDefaultAsync(x => x.Id == request.RecebimentoId, ct)
            ?? throw new KeyNotFoundException("Recebimento nao encontrado.");
        if (receipt.Estornado) throw new InvalidOperationException("Recebimento ja estornado.");
        if (string.IsNullOrWhiteSpace(request.MotivoEstorno)) throw new InvalidOperationException("Motivo do estorno e obrigatorio.");
        receipt.Estornado = true; receipt.DataEstorno = DateTime.UtcNow; receipt.UsuarioEstornoId = request.UsuarioEstornoId;
        receipt.MotivoEstorno = request.MotivoEstorno.Trim();
        FinanceiroCalculations.Recalculate(receipt.ContaReceber, DateTime.UtcNow); receipt.ContaReceber.DataAtualizacao = DateTime.UtcNow;
        var faturamento = await db.Faturamentos.Include(x => x.ContasReceber).ThenInclude(x => x.Recebimentos)
            .SingleAsync(x => x.Id == receipt.ContaReceber.FaturamentoId, ct);
        FinanceiroCalculations.RecalculatePaymentStatus(faturamento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(receipt.ContaReceber);
    }
}

public sealed class SalvarConvenioProcedimentoPrecoCommandHandler(IAppDbContext db, IClinicaContext tenant)
    : IRequestHandler<SalvarConvenioProcedimentoPrecoCommand, ConvenioProcedimentoPrecoDto>
{
    public async Task<ConvenioProcedimentoPrecoDto> Handle(SalvarConvenioProcedimentoPrecoCommand request, CancellationToken ct)
    {
        if (request.ValorNegociado < 0 || request.PercentualPrincipal < 0 || request.PercentualAuxiliar1 < 0 || request.PercentualAuxiliar2 < 0
            || request.VigenciaFinal < request.VigenciaInicio)
            throw new InvalidOperationException("Valores ou vigencia do preco sao invalidos.");
        var code = request.CbhpmCodigo.Trim();
        var overlaps = await db.ConvenioProcedimentoPrecos.AnyAsync(x => x.Id != request.Id
            && x.ConvenioId == request.ConvenioId && x.CbhpmCodigo == code && x.Ativo && request.Ativo
            && x.VigenciaInicio <= (request.VigenciaFinal ?? DateTime.MaxValue)
            && (x.VigenciaFinal == null || x.VigenciaFinal >= request.VigenciaInicio), ct);
        if (overlaps) throw new InvalidOperationException("Ja existe preco ativo com vigencia sobreposta para o convenio e procedimento.");
        var item = request.Id.HasValue
            ? await db.ConvenioProcedimentoPrecos.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
                ?? throw new KeyNotFoundException("Preco nao encontrado.")
            : new ConvenioProcedimentoPreco { ClinicaId = tenant.GetRequiredClinicaId() };
        item.ConvenioId = request.ConvenioId; item.CbhpmCodigo = code; item.ValorNegociado = request.ValorNegociado;
        item.PercentualPrincipal = request.PercentualPrincipal; item.PercentualAuxiliar1 = request.PercentualAuxiliar1;
        item.PercentualAuxiliar2 = request.PercentualAuxiliar2; item.VigenciaInicio = request.VigenciaInicio;
        item.VigenciaFinal = request.VigenciaFinal; item.Ativo = request.Ativo; item.DataAtualizacao = DateTime.UtcNow;
        if (!request.Id.HasValue) db.ConvenioProcedimentoPrecos.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    internal static ConvenioProcedimentoPrecoDto ToDto(ConvenioProcedimentoPreco x) => new(x.Id, x.ConvenioId,
        x.CbhpmCodigo, x.ValorNegociado, x.PercentualPrincipal, x.PercentualAuxiliar1, x.PercentualAuxiliar2,
        x.VigenciaInicio, x.VigenciaFinal, x.Ativo);
}

public sealed class ListarConvenioProcedimentoPrecosQueryHandler(IAppDbContext db)
    : IRequestHandler<ListarConvenioProcedimentoPrecosQuery, List<ConvenioProcedimentoPrecoDto>>
{
    public async Task<List<ConvenioProcedimentoPrecoDto>> Handle(ListarConvenioProcedimentoPrecosQuery request, CancellationToken ct)
    {
        var query = db.ConvenioProcedimentoPrecos.AsNoTracking();
        if (request.ConvenioId.HasValue) query = query.Where(x => x.ConvenioId == request.ConvenioId);
        if (!string.IsNullOrWhiteSpace(request.CbhpmCodigo)) query = query.Where(x => x.CbhpmCodigo == request.CbhpmCodigo.Trim());
        return (await query.OrderBy(x => x.CbhpmCodigo).ThenByDescending(x => x.VigenciaInicio).ToListAsync(ct))
            .Select(SalvarConvenioProcedimentoPrecoCommandHandler.ToDto).ToList();
    }
}
