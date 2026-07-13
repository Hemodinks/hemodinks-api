namespace HemodinksAPI.Domain.Models;

public class Observacao : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;

    public int AutorUserId { get; set; }
    public User AutorUser { get; set; } = null!;

    public int DestinatarioUserId { get; set; }
    public User DestinatarioUser { get; set; } = null!;

    public int? ObservacaoPaiId { get; set; }
    public Observacao? ObservacaoPai { get; set; }

    public string Texto { get; set; } = null!;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataLeitura { get; set; }

    public int? MedicoUserId { get; set; }
    public string? Medico { get; set; }

    public int? MedicoAuxiliar1UserId { get; set; }
    public string? MedicoAuxiliar1 { get; set; }

    public int? MedicoAuxiliar2UserId { get; set; }
    public string? MedicoAuxiliar2 { get; set; }
}
