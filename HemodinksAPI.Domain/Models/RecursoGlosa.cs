namespace HemodinksAPI.Domain.Models;

public class RecursoGlosa : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int GlosaId { get; set; }
    public Glosa Glosa { get; set; } = null!;
    public DateTime? DataEnvio { get; set; }
    public string Justificativa { get; set; } = null!;
    public decimal ValorRecorrido { get; set; }
    public DateTime? DataResposta { get; set; }
    public decimal ValorRecuperado { get; set; }
    public RecursoGlosaStatus Status { get; set; } = RecursoGlosaStatus.EmPreparacao;
    public string? Observacao { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}

