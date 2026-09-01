using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class GerarContaReceberCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant) : IRequestHandler<GerarContaReceberCommand, ContaReceberDto>
{
    public async Task<ContaReceberDto> Handle(GerarContaReceberCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NumeroDocumento)
            || string.IsNullOrWhiteSpace(request.Descricao))
        {
            throw new InvalidOperationException("Numero do documento e descricao sao obrigatorios.");
        }

        var document = request.NumeroDocumento.Trim();
        var existing = await db.ContasReceber.Include(x => x.Paciente).Include(x => x.Recebimentos)
            .SingleOrDefaultAsync(x => x.FaturamentoId == request.FaturamentoId && x.NumeroDocumento == document, ct);
        if (existing != null) return FinanceiroMapper.ToDto(existing);
        var f = await db.Faturamentos.Include(x => x.AtendimentoCirurgico).ThenInclude(x => x.Paciente)
            .SingleOrDefaultAsync(x => x.Id == request.FaturamentoId, ct) ?? throw new KeyNotFoundException("Faturamento nao encontrado.");
        if (f.Status is FaturamentoStatus.Rascunho or FaturamentoStatus.Cancelado)
            throw new InvalidOperationException("O faturamento precisa estar pronto para gerar conta.");
        var original = request.ValorOriginal ?? f.ValorApresentado;
        var availableAdjusted = f.DataRetorno.HasValue || f.ValorGlosado > 0
            ? f.ValorReconhecido
            : f.ValorApresentado;
        var adjusted = request.ValorAjustado ?? availableAdjusted;
        if (original < 0 || adjusted < 0) throw new InvalidOperationException("Valores da conta sao invalidos.");
        var existingTotals = await db.ContasReceber.Where(x => x.FaturamentoId == f.Id && x.Status != ContaReceberStatus.Cancelado)
            .Select(x => new { x.ValorOriginal, x.ValorAjustado }).ToListAsync(ct);
        if (existingTotals.Sum(x => x.ValorOriginal) + original > f.ValorApresentado
            || existingTotals.Sum(x => x.ValorAjustado) + adjusted > availableAdjusted)
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

public sealed class ListarContasReceberQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ListarContasReceberQuery, List<ContaReceberDto>>
{
    public async Task<List<ContaReceberDto>> Handle(ListarContasReceberQuery request, CancellationToken ct)
    {
        var accounts = await db.ContasReceber.Include(x => x.Paciente).Include(x => x.Recebimentos)
            .OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro)
            .ThenByDescending(x => x.Id).ToListAsync(ct);
        var changed = false;
        foreach (var account in accounts)
        {
            var old = account.Status; FinanceiroCalculations.Recalculate(account, DateTime.UtcNow); changed |= old != account.Status;
        }
        if (changed) await db.SaveChangesAsync(ct);
        return accounts.Select(FinanceiroMapper.ToDto).ToList();
    }
}

public sealed class RegistrarRecebimentoCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant) : IRequestHandler<RegistrarRecebimentoCommand, ContaReceberDto>
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

public sealed class EstornarRecebimentoCommandHandler(IFinanceFeatureDbContext db) : IRequestHandler<EstornarRecebimentoCommand, ContaReceberDto>
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


