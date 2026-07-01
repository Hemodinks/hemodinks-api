using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Application.Features.Licencas;

public interface ILicencaService
{
    Task<LicencaDto?> GetCurrentAsync(CurrentUserContext currentUser, CancellationToken cancellationToken);

    Task<LicencaDto> GetOrCreateForMedicoAsync(int userId, CancellationToken cancellationToken);

    Task<LicencaDto> UpdateAsync(int userId, UpdateLicencaRequest request, CancellationToken cancellationToken);

    Task<LicencaDto> LiberarCompletaAsync(int userId, LiberarLicencaCompletaRequest request, CancellationToken cancellationToken);

    Task<bool> HasFeatureAsync(CurrentUserContext currentUser, string feature, CancellationToken cancellationToken);
}
