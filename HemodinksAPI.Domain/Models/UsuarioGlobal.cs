namespace HemodinksAPI.Domain.Models;

public class UsuarioGlobal
{
    public int Id { get; set; }

    public Guid SecurityVersion { get; set; }

    public bool TemporaryPasswordRecovery { get; set; }

    public string Nome { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Senha { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public int TentativasLoginFalhas { get; set; }

    public DateTime? UltimaFalhaLoginEm { get; set; }

    public DateTime? BloqueadoAte { get; set; }

    // #150 MFA (TOTP). Segredos so cifrados (Data Protection); nunca em claro, log ou resposta apos a ativacao.
    public bool MfaHabilitado { get; set; }

    public string? MfaSegredoProtegido { get; set; }

    // Segredo gerado no setup e ainda nao confirmado por um codigo valido.
    public string? MfaSegredoPendenteProtegido { get; set; }

    public DateTime? MfaHabilitadoEm { get; set; }

    // Ultimo passo TOTP aceito (anti-replay do mesmo codigo).
    public long? MfaUltimoPassoUsado { get; set; }

    public ICollection<UsuarioClinica> Clinicas { get; set; } = new List<UsuarioClinica>();
}
