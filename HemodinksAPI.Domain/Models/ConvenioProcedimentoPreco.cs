namespace HemodinksAPI.Domain.Models;

public class ConvenioProcedimentoPreco : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int ConvenioId { get; set; }
    public Convenio Convenio { get; set; } = null!;
    public string CbhpmCodigo { get; set; } = null!;
    public decimal ValorNegociado { get; set; }
    public decimal PercentualPrincipal { get; set; } = 100m;
    public decimal PercentualAuxiliar1 { get; set; }
    public decimal PercentualAuxiliar2 { get; set; }
    public DateTime VigenciaInicio { get; set; }
    public DateTime? VigenciaFinal { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}
