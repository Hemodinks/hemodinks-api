using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Licencas;

internal static class LicencaMutations
{
    public static Licenca CreateTrial(int userId, DateTime now, int trialDays)
    {
        return new Licenca
        {
            UserId = userId,
            Plano = LicencaPlanos.Trial,
            Status = LicencaStatus.Ativa,
            DataInicioTrial = now,
            DataFimTrial = now.AddDays(Math.Max(1, trialDays)),
            DataCadastro = now
        };
    }

    public static void ApplyUpdate(Licenca licenca, UpdateLicencaRequest request, DateTime now)
    {
        licenca.Plano = LicencaValueParser.NormalizePlano(request.Plano ?? licenca.Plano);
        licenca.Status = LicencaValueParser.NormalizeStatus(request.Status ?? licenca.Status);

        if (request.DataFimTrial.HasValue)
        {
            licenca.DataFimTrial = request.DataFimTrial.Value.ToUniversalTime();
        }

        if (request.LimparDataFimLicenca)
        {
            licenca.DataFimLicenca = null;
        }
        else if (request.DataFimLicenca.HasValue)
        {
            licenca.DataFimLicenca = request.DataFimLicenca.Value.ToUniversalTime();
        }

        if (request.FeaturesLiberadas != null)
        {
            licenca.FeaturesLiberadas = LicencaValueParser.SerializeFeatures(request.FeaturesLiberadas);
        }

        licenca.Observacoes = LicencaValueParser.TrimToNull(request.Observacoes);
        licenca.DataAtualizacao = now;
    }

    public static void ApplyFullRelease(Licenca licenca, LiberarLicencaCompletaRequest request, DateTime now)
    {
        licenca.Plano = LicencaPlanos.Completa;
        licenca.Status = LicencaStatus.Ativa;
        licenca.DataFimLicenca = request.DataFimLicenca?.ToUniversalTime();
        licenca.FeaturesLiberadas = null;
        licenca.Observacoes = LicencaValueParser.TrimToNull(request.Observacoes) ?? licenca.Observacoes;
        licenca.DataAtualizacao = now;
    }
}
