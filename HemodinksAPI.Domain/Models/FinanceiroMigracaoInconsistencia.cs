namespace HemodinksAPI.Domain.Models;

public class FinanceiroMigracaoInconsistencia : IClinicaOwnedEntity
{
    public long Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;
    public string Campo { get; set; } = null!;
    public string ValorOriginal { get; set; } = null!;
    public string Motivo { get; set; } = null!;
    public bool Resolvida { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataResolucao { get; set; }
}
