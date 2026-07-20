namespace HemodinksAPI.Domain.Models;

public static class ClinicaAssinaturaStatus
{
    public const string Trial = "Trial";
    public const string Ativa = "Ativa";
    public const string Suspensa = "Suspensa";
    public const string Cancelada = "Cancelada";
}

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

    public string Plano { get; set; } = "Trial";

    public string AssinaturaStatus { get; set; } = ClinicaAssinaturaStatus.Trial;

    public DateTime? TrialAte { get; set; }

    public DateTime? AssinaturaValidaAte { get; set; }

    public int? LimiteUsuarios { get; set; }
}
