namespace HemodinksAPI.Domain.Models;

public class UsuarioGlobal
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Senha { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public ICollection<UsuarioClinica> Clinicas { get; set; } = new List<UsuarioClinica>();
}
