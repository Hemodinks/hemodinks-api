namespace HemodinksAPI.Domain.Models;

public class Paciente
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime? Data { get; set; }
    public string NomePaciente { get; set; } = null!;
    public string? Diagnostico { get; set; }
    public int? HospitalId { get; set; }
    public Hospital? HospitalReferencia { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public User? MedicoUser { get; set; }
    public string? Medico { get; set; }
    public int? MedicoAuxiliar1UserId { get; set; }
    public User? MedicoAuxiliar1User { get; set; }
    public string? MedicoAuxiliar1 { get; set; }
    public int? MedicoAuxiliar2UserId { get; set; }
    public User? MedicoAuxiliar2User { get; set; }
    public string? MedicoAuxiliar2 { get; set; }
    public int? ConvenioId { get; set; }
    public Convenio? ConvenioReferencia { get; set; }
    public string? Convenio { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public ICollection<PacienteProcedimento> Procedimentos { get; set; } = new List<PacienteProcedimento>();
    public ICollection<PacienteArquivo> Arquivos { get; set; } = new List<PacienteArquivo>();
}
