using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Tenancy;

public static class ClinicaContextFactory
{
    public static ClinicaContext CreateDefaultResolved()
    {
        var context = new ClinicaContext();
        context.SetCurrent(Clinica.DefaultId, Clinica.DefaultSlug);
        return context;
    }
}
