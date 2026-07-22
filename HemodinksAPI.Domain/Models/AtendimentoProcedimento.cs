namespace HemodinksAPI.Domain.Models;

public class AtendimentoProcedimento : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int AtendimentoCirurgicoId { get; set; }
    public AtendimentoCirurgico AtendimentoCirurgico { get; set; } = null!;
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string Descricao { get; set; } = null!;
    public decimal Quantidade { get; set; } = 1m;
    public decimal PesoPercentual { get; set; } = 100m;
    public decimal? ValorReferencia { get; set; }
    public decimal? ValorNegociado { get; set; }
    public int Ordem { get; set; } = 1;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}

