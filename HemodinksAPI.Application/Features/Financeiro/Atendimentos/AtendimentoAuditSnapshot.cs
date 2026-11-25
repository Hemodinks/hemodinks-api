using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

// Campos do atendimento comparados na trilha quando "Auditar atendimentos" esta ligado. Referencias
// (hospital, convenio, equipe, fornecedor) viram nomes so para os campos que mudaram, sob o filtro de tenant.
// Diagnostico, tratamento, motivo da glosa e observacao so como "alterado".
internal sealed record AtendimentoAuditSnapshot(
    DateTime DataProcedimento,
    TipoCirurgia TipoCirurgia,
    AtendimentoCirurgicoStatus Status,
    int? HospitalId,
    int? ConvenioId,
    int? OpmeFornecedorId,
    int? MedicoResponsavelId,
    int? MedicoAuxiliar1Id,
    int? MedicoAuxiliar2Id,
    string? NumeroAutorizacao,
    decimal? ValorGlosa,
    string? MotivoGlosa,
    string? Diagnostico,
    string? Tratamento,
    string? Observacao)
{
    public static AtendimentoAuditSnapshot Capture(AtendimentoCirurgico item) => new(
        item.DataProcedimento, item.TipoCirurgia, item.Status, item.HospitalId, item.ConvenioId, item.OpmeFornecedorId,
        item.MedicoResponsavelId, item.MedicoAuxiliar1Id, item.MedicoAuxiliar2Id, item.NumeroAutorizacao,
        item.ValorGlosa, item.MotivoGlosa, item.Diagnostico, item.TratamentoMedico, item.Observacao);

    public static async Task<ClinicAuditDetail> CompareAsync(IFinanceFeatureDbContext db,
        AtendimentoAuditSnapshot antes, AtendimentoAuditSnapshot depois, CancellationToken ct)
    {
        var detalhe = new ClinicAuditDetail()
            .Campo("Data do procedimento", antes.DataProcedimento, depois.DataProcedimento)
            .Campo("Tipo de cirurgia", antes.TipoCirurgia, depois.TipoCirurgia)
            .Campo("Status", antes.Status, depois.Status);
        if (antes.HospitalId != depois.HospitalId)
            detalhe.Campo("Hospital", await HospitalAsync(db, antes.HospitalId, ct), await HospitalAsync(db, depois.HospitalId, ct));
        if (antes.ConvenioId != depois.ConvenioId)
            detalhe.Campo("Convenio", await ConvenioAsync(db, antes.ConvenioId, ct), await ConvenioAsync(db, depois.ConvenioId, ct));
        if (antes.MedicoResponsavelId != depois.MedicoResponsavelId)
            detalhe.Campo("Cirurgiao", await UserAsync(db, antes.MedicoResponsavelId, ct), await UserAsync(db, depois.MedicoResponsavelId, ct));
        if (antes.MedicoAuxiliar1Id != depois.MedicoAuxiliar1Id)
            detalhe.Campo("Auxiliar 1", await UserAsync(db, antes.MedicoAuxiliar1Id, ct), await UserAsync(db, depois.MedicoAuxiliar1Id, ct));
        if (antes.MedicoAuxiliar2Id != depois.MedicoAuxiliar2Id)
            detalhe.Campo("Auxiliar 2", await UserAsync(db, antes.MedicoAuxiliar2Id, ct), await UserAsync(db, depois.MedicoAuxiliar2Id, ct));
        if (antes.OpmeFornecedorId != depois.OpmeFornecedorId)
            detalhe.Campo("Fornecedor OPME", await OpmeAsync(db, antes.OpmeFornecedorId, ct), await OpmeAsync(db, depois.OpmeFornecedorId, ct));
        return detalhe
            .Campo("Autorizacao", antes.NumeroAutorizacao, depois.NumeroAutorizacao)
            .Campo("Valor da glosa", antes.ValorGlosa, depois.ValorGlosa)
            .Texto("Motivo da glosa", antes.MotivoGlosa, depois.MotivoGlosa)
            .Texto("Diagnostico", antes.Diagnostico, depois.Diagnostico)
            .Texto("Tratamento medico", antes.Tratamento, depois.Tratamento)
            .Texto("Observacao", antes.Observacao, depois.Observacao);
    }

    private static Task<string?> HospitalAsync(IFinanceFeatureDbContext db, int? id, CancellationToken ct) =>
        id is null ? Task.FromResult<string?>(null)
            : db.Hospitais.AsNoTracking().Where(x => x.Id == id).Select(x => (string?)x.Nome).FirstOrDefaultAsync(ct);

    private static Task<string?> ConvenioAsync(IFinanceFeatureDbContext db, int? id, CancellationToken ct) =>
        id is null ? Task.FromResult<string?>(null)
            : db.Convenios.AsNoTracking().Where(x => x.IdConvenio == id).Select(x => (string?)x.DescricaoConvenio).FirstOrDefaultAsync(ct);

    private static Task<string?> OpmeAsync(IFinanceFeatureDbContext db, int? id, CancellationToken ct) =>
        id is null ? Task.FromResult<string?>(null)
            : db.OPME.AsNoTracking().Where(x => x.IdFornecedor == id).Select(x => (string?)x.Fornecedor).FirstOrDefaultAsync(ct);

    private static Task<string?> UserAsync(IFinanceFeatureDbContext db, int? id, CancellationToken ct) =>
        id is null ? Task.FromResult<string?>(null)
            : db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => (string?)x.Nome).FirstOrDefaultAsync(ct);
}
