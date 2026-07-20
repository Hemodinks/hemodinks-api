namespace HemodinksAPI.Domain.Models;

public class Perfil
{
    public const int AdministradorId = 1;
    public const int MedicosId = 2;
    public const int PacientesId = 3;
    public const int ControllerId = 4;
    public const int SuperAdministradorId = 5;

    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public ICollection<User> Users { get; set; } = new List<User>();

    public static bool IsAdministradorOuSuper(int perfilId)
    {
        return perfilId is AdministradorId or SuperAdministradorId;
    }
}
