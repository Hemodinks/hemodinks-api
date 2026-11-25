namespace HemodinksAPI.Domain.Models;

// #368: trilha de quem viu ou alterou dado de paciente. Nunca guarda nome, CPF, conteudo clinico,
// query string nem payload: so identificadores, acao e resultado. Excecao decidida pelo responsavel: Detalhe
// guarda o motivo informado (inativar/reativar/cancelar) e, com "Auditar atendimentos" ligado, antes/depois
// de campos estruturados do cadastro cirurgico (status, datas, hospital, convenio, equipe, procedimentos,
// pagamento). Texto clinico livre nunca: so "alterado". Apagado pela retencao (AuditoriaClinica:RetencaoDias). Sem FK para usuario ou paciente
// de proposito, para a trilha sobreviver a exclusoes.
public class AuditoriaClinica : IClinicaOwnedEntity
{
    public long Id { get; set; }

    public int ClinicaId { get; set; }

    public Clinica Clinica { get; set; } = null!;

    public int? UsuarioId { get; set; }

    public int? EquipeOperadorId { get; set; }

    public string Acao { get; set; } = null!;

    public string Recurso { get; set; } = null!;

    public string? RecursoId { get; set; }

    // Paciente afetado (tambem em acoes de atendimento), para a tela geral filtrar como a de Pacientes.
    public int? PacienteId { get; set; }

    // JSON com o motivo (inativar/reativar/cancelar) ou alteracoes antes/depois de campos estruturados.
    // Texto livre clinico (diagnostico, tratamento, observacoes) entra so como "alterado", sem conteudo.
    public string? Detalhe { get; set; }

    public int StatusCode { get; set; }

    public bool Sucesso { get; set; }

    public string? IpTruncado { get; set; }

    public DateTime DataUtc { get; set; } = DateTime.UtcNow;
}
