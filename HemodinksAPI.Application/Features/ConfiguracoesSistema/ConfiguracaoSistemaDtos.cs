namespace HemodinksAPI.Application.Features.ConfiguracoesSistema;

public sealed class ConfiguracaoSistemaDto
{
    public int Id { get; set; }

    public string NomeEmpresa { get; set; } = string.Empty;

    public string? FotoEmpresa { get; set; }

    public DateTime DataCadastro { get; set; }

    public DateTime? DataAtualizacao { get; set; }
}
