namespace HemodinksAPI.Domain.Models;

public class GrupoMedicoUsuario : IClinicaOwnedEntity
{
    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public int GrupoMedicoId { get; set; }

    public GrupoMedico GrupoMedico { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}
