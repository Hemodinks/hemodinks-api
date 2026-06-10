namespace HemodinksAPI.Api.Models;

public class CbhpmGeral
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Procedimento { get; set; } = null!;
    public string? Porte { get; set; }
    public decimal? CustoOperacional { get; set; }
    public decimal? ValorReferencia { get; set; }
    public string? Capitulo { get; set; }
    public string? Grupo { get; set; }
    public int? PaginaPdf { get; set; }
}
