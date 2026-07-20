namespace HemodinksAPI.Domain.Models;

public class AuditoriaPlataforma
{
    public long Id { get; set; }

    public int UsuarioGlobalId { get; set; }

    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;

    public int? ClinicaId { get; set; }

    public Clinica? Clinica { get; set; }

    public int? UserId { get; set; }

    public string Acao { get; set; } = null!;

    public string Recurso { get; set; } = null!;

    public string? EntidadeId { get; set; }

    public string? DetalhesJson { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    public string? RequestId { get; set; }

    public bool Sucesso { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}
