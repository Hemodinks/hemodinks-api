using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Licencas;

internal static class LicencaMapper
{
    public static LicencaDto ToDto(Licenca licenca, DateTime now)
    {
        var featuresLiberadas = LicencaValueParser.ParseFeatures(licenca.FeaturesLiberadas);
        var featuresEfetivas = LicencaFeatureRules.GetEffectiveFeatures(licenca, now).ToList();
        var trialRemainingDays = Math.Max(0, (int)Math.Ceiling((licenca.DataFimTrial - now).TotalDays));

        return new LicencaDto
        {
            Id = licenca.Id,
            UserId = licenca.UserId,
            Plano = licenca.Plano,
            Status = licenca.Status,
            DataInicioTrial = licenca.DataInicioTrial,
            DataFimTrial = licenca.DataFimTrial,
            DataFimLicenca = licenca.DataFimLicenca,
            FeaturesLiberadas = featuresLiberadas,
            FeaturesEfetivas = featuresEfetivas,
            TrialExpirado = licenca.DataFimTrial < now,
            LicencaExpirada = licenca.DataFimLicenca.HasValue && licenca.DataFimLicenca.Value < now,
            Ativa = LicencaFeatureRules.IsLicenseActive(licenca, now),
            AcessoCompleto = LicencaFeatureRules.HasFullAccess(licenca, now),
            DiasRestantesTrial = trialRemainingDays,
            Observacoes = licenca.Observacoes,
            DataCadastro = licenca.DataCadastro,
            DataAtualizacao = licenca.DataAtualizacao
        };
    }

    public static LicencaDto CreateUnrestrictedDto(int userId)
    {
        return new LicencaDto
        {
            UserId = userId,
            ControleAplicavel = false,
            Plano = LicencaPlanos.Completa,
            Status = LicencaStatus.Ativa,
            FeaturesEfetivas = LicencaFeatures.Todas.ToList(),
            Ativa = true,
            AcessoCompleto = true
        };
    }
}
