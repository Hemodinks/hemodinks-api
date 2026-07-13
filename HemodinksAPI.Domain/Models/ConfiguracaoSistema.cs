namespace HemodinksAPI.Domain.Models;

public class ConfiguracaoSistema : IClinicaOwnedEntity
{
    public const int DefaultId = 1;

    public const string DefaultNomeEmpresa = "Hemodinks";

    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public string NomeEmpresa { get; set; } = DefaultNomeEmpresa;

    public string? FotoEmpresa { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
