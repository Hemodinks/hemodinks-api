namespace HemodinksAPI.Domain.Models;

public class Opme : IClinicaOwnedEntity
{
    public int IdFornecedor { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public string Fornecedor { get; set; } = null!;
    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
