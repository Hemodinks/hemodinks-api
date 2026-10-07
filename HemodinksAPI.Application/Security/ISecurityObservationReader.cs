namespace HemodinksAPI.Application.Security;

public sealed record SecurityObservationPage(IReadOnlyList<SecurityObservation> Items, int Page, int PageSize);
public interface ISecurityObservationReader
{
    SecurityObservationPage Read(int page, int pageSize, int? clinicId);
}
