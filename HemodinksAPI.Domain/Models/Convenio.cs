namespace HemodinksAPI.Domain.Models;

public class Convenio : IClinicaOwnedEntity
{
    public int IdConvenio { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public string DescricaoConvenio { get; set; } = null!;
    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
