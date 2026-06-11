using System.Threading.Tasks;

namespace HemodinksAPI.Application.Services;

public interface INotificationService
{
    Task SendNotificationToUserAsync(int userId, string title, string message);

    Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message);
}
