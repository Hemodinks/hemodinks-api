namespace HemodinksAPI.Domain.Models;

public class AtendimentoCirurgico : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;
    public DateTime DataProcedimento { get; set; }
    public int? HospitalId { get; set; }
    public Hospital? Hospital { get; set; }
    public int? ConvenioId { get; set; }
    public Convenio? Convenio { get; set; }
    public int MedicoResponsavelId { get; set; }
    public User MedicoResponsavel { get; set; } = null!;
    public int? MedicoAuxiliar1Id { get; set; }
    public User? MedicoAuxiliar1 { get; set; }
    public int? MedicoAuxiliar2Id { get; set; }
    public User? MedicoAuxiliar2 { get; set; }
    public string? Diagnostico { get; set; }
    public string? TratamentoMedico { get; set; }
    public string? NumeroAutorizacao { get; set; }
    public AtendimentoCirurgicoStatus Status { get; set; } = AtendimentoCirurgicoStatus.Planejado;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public ICollection<AtendimentoProcedimento> Procedimentos { get; set; } = new List<AtendimentoProcedimento>();
    public ICollection<Faturamento> Faturamentos { get; set; } = new List<Faturamento>();
}

