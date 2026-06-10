namespace HemodinksAPI.Api.Models;

/// <summary>
/// Modelo de usuário do sistema
/// </summary>
public class User
{
    /// <summary>
    /// Identificador único do usuário
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Nome completo do usuário
    /// </summary>
    public string Nome { get; set; } = null!;

    /// <summary>
    /// Telefone do usuário com código de país
    /// </summary>
    public string Telefone { get; set; } = null!;

    public string? Cpf { get; set; }

    public string? Crm { get; set; }

    public string? CrmUf { get; set; }

    /// <summary>
    /// Foto de perfil do usuario em data URL.
    /// </summary>
    public string? FotoPerfil { get; set; }

    /// <summary>
    /// Email do usuário
    /// </summary>
    public string Email { get; set; } = null!;

    /// <summary>
    /// Senha com hash do usuário
    /// </summary>
    public string Senha { get; set; } = null!;

    /// <summary>
    /// Data de cadastro do usuário
    /// </summary>
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    /// <summary>
    /// Data de nascimento do usuário
    /// </summary>
    public DateTime DataNascimento { get; set; }

    /// <summary>
    /// Indica se o usuário está ativo
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Indica se o usuário precisa trocar a senha no próximo login
    /// </summary>
    public bool PrecisaTrocarSenha { get; set; } = true;

    public int PerfilId { get; set; } = Perfil.MedicosId;

    public Perfil Perfil { get; set; } = null!;

    public Paciente? Paciente { get; set; }

    public Licenca? Licenca { get; set; }

    public ICollection<Event> Events { get; set; } = new List<Event>();

    public ICollection<Event> MedicalEvents { get; set; } = new List<Event>();

    public ICollection<UserArquivo> Arquivos { get; set; } = new List<UserArquivo>();
}
