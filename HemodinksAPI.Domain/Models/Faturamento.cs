namespace HemodinksAPI.Domain.Models;

public class Faturamento : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int AtendimentoCirurgicoId { get; set; }
    public AtendimentoCirurgico AtendimentoCirurgico { get; set; } = null!;
    public int? ConvenioId { get; set; }
    public Convenio? Convenio { get; set; }
    public string? NumeroGuia { get; set; }
    public string? NumeroLote { get; set; }
    public DateTime Competencia { get; set; }
    public DateTime? DataEnvio { get; set; }
    public DateTime? DataRetorno { get; set; }
    public decimal ValorApresentado { get; set; }
    public decimal ValorGlosado { get; set; }
    public decimal ValorGlosaRecuperada { get; set; }
    public decimal ValorReconhecido { get; set; }
    public FaturamentoStatus Status { get; set; } = FaturamentoStatus.Rascunho;
    public string? Observacao { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<FaturamentoItem> Itens { get; set; } = new List<FaturamentoItem>();
    public ICollection<Glosa> Glosas { get; set; } = new List<Glosa>();
    public ICollection<ContaReceber> ContasReceber { get; set; } = new List<ContaReceber>();
}
