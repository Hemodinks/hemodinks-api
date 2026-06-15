namespace HemodinksAPI.Domain.Models;

public class Opme
{
    public int IdFornecedor { get; set; }
    public string Fornecedor { get; set; } = null!;
    public ICollection<Paciente> Pacientes { get; set; } = new List<Paciente>();
}
