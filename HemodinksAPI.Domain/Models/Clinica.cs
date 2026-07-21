namespace HemodinksAPI.Domain.Models;

public static class ClinicaPlanos
{
    public const string Trial = "Trial";
    public const string Completa = "Completa";
    public const string Parcial = "Parcial";
}

public static class ClinicaModulos
{
    public const string Usuarios = "usuarios";
    public const string Pacientes = "pacientes";
    public const string Faturamento = "faturamento";
    public const string GruposMedicos = "grupos-medicos";
    public const string Agenda = "agenda";

    public static readonly IReadOnlyList<string> Todos =
    [
        Usuarios,
        Pacientes,
        Faturamento,
        GruposMedicos,
        Agenda
    ];

    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => Todos.Contains(item, StringComparer.OrdinalIgnoreCase))
            .Select(item => Todos.First(allowed => allowed.Equals(item, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetEffective(string plano, string? value)
    {
        return plano.Equals(ClinicaPlanos.Parcial, StringComparison.OrdinalIgnoreCase)
            ? Parse(value)
            : Todos;
    }
}

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

    public string? FotoClinica { get; set; }

    public bool Ativa { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public string Plano { get; set; } = ClinicaPlanos.Trial;

    public string? ModulosLiberados { get; set; }

    public string AssinaturaStatus { get; set; } = ClinicaAssinaturaStatus.Trial;

    public DateTime? TrialAte { get; set; }

    public DateTime? AssinaturaValidaAte { get; set; }

    public int? LimiteUsuarios { get; set; }
}
