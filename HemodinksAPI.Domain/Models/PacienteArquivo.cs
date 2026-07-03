namespace HemodinksAPI.Domain.Models;

public class PacienteArquivo : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;
    public string NomeOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string Url { get; set; } = null!;
    public DateTime DataUpload { get; set; } = DateTime.UtcNow;
}
