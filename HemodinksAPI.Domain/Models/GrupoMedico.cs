namespace HemodinksAPI.Domain.Models;

public class GrupoMedico
{
    public int Id { get; set; }

    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public ICollection<GrupoMedicoUsuario> Membros { get; set; } = new List<GrupoMedicoUsuario>();
}
