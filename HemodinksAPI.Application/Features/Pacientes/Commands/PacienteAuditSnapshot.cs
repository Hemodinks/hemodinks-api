using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

// Campos do cadastro de paciente/cirurgia comparados na trilha quando "Auditar atendimentos" esta ligado.
// CPF, e-mail, telefone e foto nunca entram; nome, diagnostico e tratamento so como "alterado".
internal sealed record PacienteAuditSnapshot(
    string? Nome,
    DateTime? DataSolicitacao,
    DateTime? DataConsolidacao,
    StatusCirurgia StatusCirurgia,
    TipoCirurgia TipoCirurgia,
    string? Diagnostico,
    string? Tratamento,
    string? Hospital,
    string? Convenio,
    string? Cirurgiao,
    string? Auxiliar1,
    string? Auxiliar2,
    string? Fornecedor,
    string? Procedimentos,
    string? Autorizacao,
    string? Pagamento,
    string? RepasseGlosa,
    bool Pago,
    DateTime? DataPagamento)
{
    public static PacienteAuditSnapshot Capture(Paciente paciente) => new(
        paciente.NomePaciente,
        paciente.Data,
        paciente.DataAtendimento,
        paciente.StatusCirurgia,
        paciente.TipoCirurgia,
        paciente.Diagnostico,
        paciente.TratamentoMedico,
        paciente.Hospital,
        paciente.Convenio,
        paciente.Medico,
        paciente.MedicoAuxiliar1,
        paciente.MedicoAuxiliar2,
        paciente.OpmeFornecedor,
        string.Join("; ", paciente.Procedimentos.OrderBy(item => item.Ordem)
            .Select(item => string.IsNullOrWhiteSpace(item.CbhpmCodigo) ? item.Procedimento : $"{item.CbhpmCodigo} {item.Procedimento}")),
        paciente.Autorizacao,
        paciente.Pagamento,
        paciente.RepasseGlosa,
        paciente.StatusPago,
        paciente.FaturamentoMedico?.DataPagamento);

    public static ClinicAuditDetail Compare(PacienteAuditSnapshot antes, PacienteAuditSnapshot depois) => new ClinicAuditDetail()
        .Texto("Nome", antes.Nome, depois.Nome)
        .Campo("Data da solicitacao", antes.DataSolicitacao, depois.DataSolicitacao)
        .Campo("Data da consolidacao", antes.DataConsolidacao, depois.DataConsolidacao)
        .Campo("Status da cirurgia", antes.StatusCirurgia, depois.StatusCirurgia)
        .Campo("Tipo de cirurgia", antes.TipoCirurgia, depois.TipoCirurgia)
        .Texto("Diagnostico", antes.Diagnostico, depois.Diagnostico)
        .Texto("Tratamento medico", antes.Tratamento, depois.Tratamento)
        .Campo("Hospital", antes.Hospital, depois.Hospital)
        .Campo("Convenio", antes.Convenio, depois.Convenio)
        .Campo("Cirurgiao", antes.Cirurgiao, depois.Cirurgiao)
        .Campo("Auxiliar 1", antes.Auxiliar1, depois.Auxiliar1)
        .Campo("Auxiliar 2", antes.Auxiliar2, depois.Auxiliar2)
        .Campo("Fornecedor OPME", antes.Fornecedor, depois.Fornecedor)
        .Campo("Procedimentos", antes.Procedimentos, depois.Procedimentos)
        .Campo("Autorizacao", antes.Autorizacao, depois.Autorizacao)
        .Campo("Pagamento", antes.Pagamento, depois.Pagamento)
        .Campo("Repasse/glosa", antes.RepasseGlosa, depois.RepasseGlosa)
        .Campo("Pago", antes.Pago, depois.Pago)
        .Campo("Data do pagamento", antes.DataPagamento, depois.DataPagamento);
}
