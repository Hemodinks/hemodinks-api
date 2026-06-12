using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Licencas;

public static class LicencaFeatures
{
    public const string DashboardVisualizar = "Dashboard.Visualizar";
    public const string PacientesVisualizar = "Pacientes.Visualizar";
    public const string PacientesGerenciar = "Pacientes.Gerenciar";
    public const string CbhpmConsultar = "Cbhpm.Consultar";

    public static readonly IReadOnlyCollection<string> Trial =
    [
        DashboardVisualizar,
        PacientesVisualizar,
        CbhpmConsultar
    ];

    public static readonly IReadOnlyCollection<string> Todas =
    [
        DashboardVisualizar,
        PacientesVisualizar,
        CbhpmConsultar
    ];
}

public static class LicencaPolicies
{
    public const string DashboardVisualizar = "Licenca.Dashboard.Visualizar";
    public const string PacientesVisualizar = "Licenca.Pacientes.Visualizar";
    public const string PacientesGerenciar = "Licenca.Pacientes.Gerenciar";
    public const string CbhpmConsultar = "Licenca.Cbhpm.Consultar";
}

public class LicencaOptions
{
    public int TrialDays { get; set; } = 14;
}

public class LicencaDto
{
    public int? Id { get; set; }
    public int UserId { get; set; }
    public bool ControleAplicavel { get; set; } = true;
    public string Plano { get; set; } = LicencaPlanos.Trial;
    public string Status { get; set; } = LicencaStatus.Ativa;
    public DateTime? DataInicioTrial { get; set; }
    public DateTime? DataFimTrial { get; set; }
    public DateTime? DataFimLicenca { get; set; }
    public IReadOnlyList<string> FeaturesLiberadas { get; set; } = [];
    public IReadOnlyList<string> FeaturesEfetivas { get; set; } = [];
    public bool TrialExpirado { get; set; }
    public bool LicencaExpirada { get; set; }
    public bool Ativa { get; set; }
    public bool AcessoCompleto { get; set; }
    public int DiasRestantesTrial { get; set; }
    public string? Observacoes { get; set; }
    public DateTime? DataCadastro { get; set; }
    public DateTime? DataAtualizacao { get; set; }
}

public class UpdateLicencaRequest
{
    public string? Plano { get; set; }
    public string? Status { get; set; }
    public DateTime? DataFimTrial { get; set; }
    public DateTime? DataFimLicenca { get; set; }
    public bool LimparDataFimLicenca { get; set; }
    public List<string>? FeaturesLiberadas { get; set; }
    public string? Observacoes { get; set; }
}

public class LiberarLicencaCompletaRequest
{
    public DateTime? DataFimLicenca { get; set; }
    public string? Observacoes { get; set; }
}
