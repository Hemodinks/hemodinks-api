using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Licencas;

internal static class LicencaFeatureRules
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public static bool HasImplicitFeatureAccess(CurrentUserContext currentUser, string feature, out bool allowed)
    {
        if (currentUser.IsAdministrador)
        {
            allowed = true;
            return true;
        }

        if (currentUser.IsPaciente)
        {
            allowed = feature is LicencaFeatures.DashboardVisualizar
                or LicencaFeatures.PacientesVisualizar
                or LicencaFeatures.CbhpmConsultar;
            return true;
        }

        if (currentUser.IsController)
        {
            allowed = feature is LicencaFeatures.DashboardVisualizar
                or LicencaFeatures.PacientesVisualizar
                or LicencaFeatures.PacientesGerenciar
                or LicencaFeatures.CbhpmConsultar;
            return true;
        }

        if (!currentUser.IsMedico)
        {
            allowed = false;
            return true;
        }

        allowed = feature is LicencaFeatures.DashboardVisualizar
            or LicencaFeatures.PacientesVisualizar
            or LicencaFeatures.PacientesGerenciar
            or LicencaFeatures.CbhpmConsultar;
        return true;
    }

    public static bool IsFeatureAllowed(Licenca licenca, string feature, DateTime now)
    {
        return GetEffectiveFeatures(licenca, now).Contains(feature, TextComparer);
    }

    public static IEnumerable<string> GetEffectiveFeatures(Licenca licenca, DateTime now)
    {
        if (!IsLicenseActive(licenca, now))
        {
            return [];
        }

        if (HasFullAccess(licenca, now))
        {
            return LicencaFeatures.Todas;
        }

        if (TextComparer.Equals(licenca.Plano, LicencaPlanos.Trial) && licenca.DataFimTrial >= now)
        {
            return LicencaFeatures.Trial
                .Concat(LicencaValueParser.ParseFeatures(licenca.FeaturesLiberadas))
                .Distinct(TextComparer);
        }

        return LicencaValueParser.ParseFeatures(licenca.FeaturesLiberadas)
            .Distinct(TextComparer);
    }

    public static bool HasFullAccess(Licenca licenca, DateTime now)
    {
        return TextComparer.Equals(licenca.Plano, LicencaPlanos.Completa)
            && IsLicenseActive(licenca, now)
            && (!licenca.DataFimLicenca.HasValue || licenca.DataFimLicenca.Value >= now);
    }

    public static bool IsLicenseActive(Licenca licenca, DateTime now)
    {
        return TextComparer.Equals(licenca.Status, LicencaStatus.Ativa)
            && (!licenca.DataFimLicenca.HasValue || licenca.DataFimLicenca.Value >= now);
    }
}
