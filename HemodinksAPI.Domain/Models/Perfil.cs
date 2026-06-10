namespace HemodinksAPI.Api.Models;

public class Perfil
{
    public const int AdministradorId = 1;
    public const int MedicosId = 2;
    public const int PacientesId = 3;

    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public ICollection<User> Users { get; set; } = new List<User>();
}
