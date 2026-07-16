namespace HemodinksAPI.Domain.Models;

public class Hospital : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public string Nome { get; set; } = null!;

    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
