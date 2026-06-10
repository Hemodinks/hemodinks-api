using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Services;

public interface IUserPatientSyncService
{
    Task EnsurePacienteForUserAsync(User user, CancellationToken cancellationToken);
}
