namespace HemodinksAPI.Api.Models;

public class Hospital
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
