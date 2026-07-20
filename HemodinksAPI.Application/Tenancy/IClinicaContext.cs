namespace HemodinksAPI.Application.Tenancy;

public interface IClinicaContext
{
    int? ClinicaId { get; }

    string? ClinicaSlug { get; }

    bool IsResolved { get; }

    bool IsPlatformScope { get; }

    int GetRequiredClinicaId();
}
