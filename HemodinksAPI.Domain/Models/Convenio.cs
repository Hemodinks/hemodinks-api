namespace HemodinksAPI.Domain.Models;

public class Convenio
{
    public int IdConvenio { get; set; }
    public string DescricaoConvenio { get; set; } = null!;
    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
