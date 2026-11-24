namespace HemodinksAPI.Domain.Models;

// #150: codigo de recuperacao de uso unico (so o hash SHA-256 e guardado).
public sealed class MfaCodigoRecuperacao
{
    public int Id { get; set; }
    public int UsuarioGlobalId { get; set; }
    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;
    public string CodigoHash { get; set; } = null!;
    public DateTime? UsadoEm { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}

// #150: desafio do segundo fator emitido depois da senha correta. Token de uso unico (so hash), curto prazo,
// limitado em tentativas e preso a SecurityVersion e a clinica do login.
public sealed class MfaLoginDesafio : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public Clinica Clinica { get; set; } = null!;
    public int UsuarioGlobalId { get; set; }
    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public Guid SecurityVersion { get; set; }
    public DateTime ExpiraEm { get; set; }
    public int Tentativas { get; set; }
    public DateTime? UtilizadoEm { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}
