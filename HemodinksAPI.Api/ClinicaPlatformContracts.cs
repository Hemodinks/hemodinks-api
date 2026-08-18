namespace HemodinksAPI.Api;

public sealed record CreateClinicaRequest(
    string Nome,
    string Slug,
    string AdministradorNome,
    string AdministradorEmail,
    string AdministradorSenha,
    string? AdministradorTelefone,
    string? Plano,
    IReadOnlyList<string>? ModulosLiberados,
    string? AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios,
    string? FotoClinica,
    CreateEquipeInicialRequest? EquipeInicial);

public sealed record CreateEquipeInicialRequest(
    string Nome,
    string Email,
    string Senha,
    string? Telefone,
    string? ModoIdentificacao);

public sealed record UpdateClinicaRequest(
    string? Nome,
    string? Slug,
    bool? Ativa,
    string? Plano,
    IReadOnlyList<string>? ModulosLiberados,
    string? AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios,
    string? FotoClinica,
    string? AdministradorNovaSenha,
    CreateEquipeInicialRequest? NovaEquipe);

public sealed record ClinicaPlatformResponse(
    int Id,
    string Nome,
    string Slug,
    string? FotoUrl,
    bool Ativa,
    string Plano,
    IReadOnlyList<string> ModulosLiberados,
    string AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios,
    int? Usuarios,
    DateTime DataCadastro,
    DateTime? DataAtualizacao);
