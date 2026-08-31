using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class CriarAtendimentoCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant)
    : IRequestHandler<CriarAtendimentoCommand, AtendimentoDto>
{
    public async Task<AtendimentoDto> Handle(CriarAtendimentoCommand request, CancellationToken ct)
    {
        var clinicaId = tenant.GetRequiredClinicaId();
        if (request.DataProcedimento == default || request.Procedimentos.Count == 0)
            throw new InvalidOperationException("Data e ao menos um procedimento sao obrigatorios.");
        if (request.ValorGlosa < 0 || request.ValorGlosa > 0 && string.IsNullOrWhiteSpace(request.MotivoGlosa))
            throw new InvalidOperationException("Informe um valor de glosa valido e o respectivo motivo.");
        var medicoResponsavelId = request.CurrentPerfilId == Perfil.MedicosId ? request.CurrentUserId : request.MedicoResponsavelId;
        var ids = new[] { medicoResponsavelId, request.MedicoAuxiliar1Id ?? 0, request.MedicoAuxiliar2Id ?? 0 }.Where(x => x > 0).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("Os medicos do atendimento devem ser distintos.");
        var paciente = await db.Pacientes.SingleOrDefaultAsync(x => x.Id == request.PacienteId, ct)
            ?? throw new KeyNotFoundException("Paciente nao encontrado.");
        var hospital = request.HospitalId.HasValue || !string.IsNullOrWhiteSpace(request.Hospital)
            ? await ClinicalReferenceResolver.ResolveHospitalAsync(db, clinicaId, request.HospitalId, request.Hospital, ct)
            : null;
        var convenio = await ClinicalReferenceResolver.ResolveConvenioAsync(db, clinicaId, request.ConvenioId, request.Convenio, ct);
        var opmeFornecedor = await ClinicalReferenceResolver.ResolveOpmeFornecedorAsync(
            db, clinicaId, request.OpmeFornecedorId, request.OpmeFornecedor, ct);
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
            ValorGlosa = request.ValorGlosa > 0 ? request.ValorGlosa : null,
            MotivoGlosa = request.ValorGlosa > 0 ? request.MotivoGlosa?.Trim() : null,
            Observacao = request.Observacao?.Trim(),
            Status = request.Status
        };

        var order = 0;
        foreach (var input in request.Procedimentos)
        {
            if (input.Quantidade <= 0 || input.PesoPercentual < 0)
                throw new InvalidOperationException("Quantidade e peso do procedimento sao invalidos.");
            int? convenioId = convenio?.Id > 0 ? convenio.Id : null;
            var procedimento = await FinanceiroProcedimentoResolver.ResolveAsync(
                db, input, convenioId, request.DataProcedimento, ct);
            if (string.IsNullOrWhiteSpace(procedimento.Descricao))
                throw new InvalidOperationException("Descricao obrigatoria para procedimento sem cadastro CBHPM.");
            atendimento.Procedimentos.Add(new AtendimentoProcedimento
            {
                ClinicaId = clinicaId, CbhpmCodigo = procedimento.Codigo,
                CbhpmPorte = procedimento.Porte,
                Descricao = procedimento.Descricao, Quantidade = input.Quantidade, PesoPercentual = input.PesoPercentual,
                ValorReferencia = procedimento.ValorReferencia, ValorNegociado = procedimento.ValorNegociado,
                Ordem = ++order
            });
        }
        db.AtendimentosCirurgicos.Add(atendimento);
        await db.SaveChangesAsync(ct);
        return FinanceiroMapper.ToDto(atendimento);
    }
}

public sealed class ListarAtendimentosQueryHandler(IFinanceFeatureDbContext db) : IRequestHandler<ListarAtendimentosQuery, List<AtendimentoDto>>
{
    public async Task<List<AtendimentoDto>> Handle(ListarAtendimentosQuery request, CancellationToken ct)
    {
        var query = db.AtendimentosCirurgicos.AsNoTracking().Include(x => x.Paciente)
            .Include(x => x.OpmeFornecedor).Include(x => x.Procedimentos).Include(x => x.Arquivos).AsQueryable();
        if (request.CurrentPerfilId == Perfil.MedicosId)
            query = query.Where(x => x.MedicoResponsavelId == request.CurrentUserId || x.MedicoAuxiliar1Id == request.CurrentUserId || x.MedicoAuxiliar2Id == request.CurrentUserId);
        if (request.PacienteId.HasValue) query = query.Where(x => x.PacienteId == request.PacienteId);
        return (await query.OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro)
            .ThenByDescending(x => x.Id).ToListAsync(ct)).Select(FinanceiroMapper.ToDto).ToList();
    }
}


