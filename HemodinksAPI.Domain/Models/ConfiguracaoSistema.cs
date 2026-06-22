namespace HemodinksAPI.Domain.Models;

public class ConfiguracaoSistema
{
    public const int DefaultId = 1;

    public int Id { get; set; }

    public string NomeEmpresa { get; set; } = "Hemodinks";

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
