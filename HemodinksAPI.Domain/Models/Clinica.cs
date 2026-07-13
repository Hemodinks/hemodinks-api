namespace HemodinksAPI.Domain.Models;

public class Clinica
{
    public const int DefaultId = 1;

    public const string DefaultNome = "HemoDinks";

    public const string DefaultSlug = "hemodinks";

    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public string Slug { get; set; } = DefaultSlug;

    public bool Ativa { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
