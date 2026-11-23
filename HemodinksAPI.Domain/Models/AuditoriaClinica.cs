namespace HemodinksAPI.Domain.Models;

// #368: trilha de quem viu ou alterou dado de paciente. Nunca guarda nome, CPF, conteudo clinico,
// query string nem payload: so identificadores, acao e resultado. Sem FK para usuario ou paciente
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

    public int StatusCode { get; set; }

    public bool Sucesso { get; set; }

    public string? IpTruncado { get; set; }

    public DateTime DataUtc { get; set; } = DateTime.UtcNow;
}
