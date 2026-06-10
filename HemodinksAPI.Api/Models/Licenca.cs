namespace HemodinksAPI.Api.Models;

public static class LicencaPlanos
{
    public const string Trial = "Trial";
    public const string Completa = "Completa";
}

public static class LicencaStatus
{
    public const string Ativa = "Ativa";
    public const string Suspensa = "Suspensa";
    public const string Cancelada = "Cancelada";
}

public class Licenca
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public string Plano { get; set; } = LicencaPlanos.Trial;

    public string Status { get; set; } = LicencaStatus.Ativa;

    public DateTime DataInicioTrial { get; set; } = DateTime.UtcNow;

    public DateTime DataFimTrial { get; set; } = DateTime.UtcNow.AddDays(14);

    public DateTime? DataFimLicenca { get; set; }

    public string? FeaturesLiberadas { get; set; }

    public string? Observacoes { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
