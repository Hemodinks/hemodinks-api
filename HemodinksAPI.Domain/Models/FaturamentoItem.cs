namespace HemodinksAPI.Domain.Models;

public class FaturamentoItem : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int FaturamentoId { get; set; }
    public Faturamento Faturamento { get; set; } = null!;
    public int? AtendimentoProcedimentoId { get; set; }
    public AtendimentoProcedimento? AtendimentoProcedimento { get; set; }
    public string? Codigo { get; set; }
    public string Descricao { get; set; } = null!;
    public decimal Quantidade { get; set; } = 1m;
    public decimal PesoPercentual { get; set; } = 100m;
    public decimal ValorUnitario { get; set; }
    public decimal ValorApresentado { get; set; }
    public decimal ValorGlosado { get; set; }
    public decimal ValorAprovado { get; set; }
    public string? MotivoGlosa { get; set; }
    public FaturamentoItemStatus Status { get; set; } = FaturamentoItemStatus.Rascunho;
    public int Ordem { get; set; } = 1;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public ICollection<Glosa> Glosas { get; set; } = new List<Glosa>();
}

