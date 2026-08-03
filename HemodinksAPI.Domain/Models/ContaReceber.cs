namespace HemodinksAPI.Domain.Models;

public class ContaReceber : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int FaturamentoId { get; set; }
    public Faturamento Faturamento { get; set; } = null!;
    public int? ConvenioId { get; set; }
    public Convenio? Convenio { get; set; }
    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;
    public string NumeroDocumento { get; set; } = null!;
    public string Descricao { get; set; } = null!;
    public DateTime Competencia { get; set; }
    public DateTime DataEmissao { get; set; }
    public DateTime DataVencimento { get; set; }
    public decimal ValorOriginal { get; set; }
    public decimal ValorAjustado { get; set; }
    public decimal ValorRecebido { get; set; }
    public decimal SaldoAberto { get; set; }
    public ContaReceberStatus Status { get; set; } = ContaReceberStatus.Previsto;
    public string? Observacao { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<Recebimento> Recebimentos { get; set; } = new List<Recebimento>();
}

