namespace HemodinksAPI.Domain.Models;

public class UsuarioClinica
{
    public int Id { get; set; }

    public int UsuarioGlobalId { get; set; }

    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;

    public int ClinicaId { get; set; }

    public Clinica Clinica { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int PerfilId { get; set; }

    public Perfil Perfil { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public bool ClinicaPadrao { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
