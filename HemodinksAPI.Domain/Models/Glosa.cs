namespace HemodinksAPI.Domain.Models;

public class Glosa : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int FaturamentoId { get; set; }
    public Faturamento Faturamento { get; set; } = null!;
    public int? FaturamentoItemId { get; set; }
    public FaturamentoItem? FaturamentoItem { get; set; }
    public string? CodigoMotivo { get; set; }
    public string DescricaoMotivo { get; set; } = null!;
    public decimal ValorGlosado { get; set; }
    public DateTime DataGlosa { get; set; }
    public GlosaStatus Status { get; set; } = GlosaStatus.Aberta;
    public string? Observacao { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public ICollection<RecursoGlosa> Recursos { get; set; } = new List<RecursoGlosa>();
}

