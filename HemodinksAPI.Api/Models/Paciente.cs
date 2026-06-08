namespace HemodinksAPI.Api.Models;

public class Paciente
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime? Data { get; set; }
    public string NomePaciente { get; set; } = null!;
    public int? HospitalId { get; set; }
    public Hospital? HospitalReferencia { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public User? MedicoUser { get; set; }
    public string? Medico { get; set; }
    public string? Convenio { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public ICollection<PacienteArquivo> Arquivos { get; set; } = new List<PacienteArquivo>();
}
