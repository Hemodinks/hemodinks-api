using HemodinksAPI.Api.Models;

namespace HemodinksAPI.Api.Services;

public interface IUserPatientSyncService
{
    Task EnsurePacienteForUserAsync(User user, CancellationToken cancellationToken);
}
