namespace HemodinksAPI.Domain.Models;

public class ConfiguracaoSistema
{
    public const int DefaultId = 1;

    public const string DefaultNomeEmpresa = "Hemodinks";

    public int Id { get; set; }

    public string NomeEmpresa { get; set; } = DefaultNomeEmpresa;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
