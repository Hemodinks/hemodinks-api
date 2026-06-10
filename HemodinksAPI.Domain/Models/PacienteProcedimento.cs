namespace HemodinksAPI.Api.Models;

public class PacienteProcedimento
{
    public int Id { get; set; }
    public int PacienteId { get; set; }
    public Paciente Paciente { get; set; } = null!;
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string Procedimento { get; set; } = null!;
    public decimal? ValorReferencia { get; set; }
    public int Ordem { get; set; }
}
