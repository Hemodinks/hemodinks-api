namespace HemodinksAPI.Domain.Models;

public class GrupoMedico : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; } = true;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public ICollection<GrupoMedicoUsuario> Membros { get; set; } = new List<GrupoMedicoUsuario>();
}
