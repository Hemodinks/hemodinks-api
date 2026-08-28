using HemodinksAPI.Application.Authorization;

namespace HemodinksAPI.Application.Tenancy;

public sealed class ClinicaContext : IClinicaContext
{
    public int? ClinicaId { get; private set; }

    public string? ClinicaSlug { get; private set; }

    public bool IsResolved => ClinicaId.HasValue;

    public bool IsPlatformScope { get; private set; }

    public void SetCurrent(int clinicaId, string clinicaSlug)
    {
        if (clinicaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clinicaId));
        }

        ClinicaId = clinicaId;
        ClinicaSlug = clinicaSlug;
        IsPlatformScope = false;
    }

    internal void SetPlatformScope()
    {
        ClinicaId = null;
        ClinicaSlug = null;
        IsPlatformScope = true;
    }

    public int GetRequiredClinicaId()
    {
        return ClinicaId
            ?? throw new InvalidOperationException("Clinica atual nao resolvida para a requisicao.");
    }

    public string GetRequiredClinicaSlug()
    {
        return ClinicaSlug
            ?? throw new InvalidOperationException("Slug da clinica atual nao resolvido para a requisicao.");
    }

    public void EnsureCurrentMatches(CurrentUserContext currentUser)
    {
        var clinicaId = ClinicaId;
        if (!clinicaId.HasValue)
        {
            throw new InvalidOperationException("Clinica atual nao resolvida para a requisicao.");
        }

        if (currentUser.ClinicaId != clinicaId.Value)
        {
            throw new UnauthorizedAccessException("Usuario autenticado nao pertence a clinica resolvida.");
        }
    }
}
