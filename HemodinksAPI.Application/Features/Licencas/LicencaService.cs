using HemodinksAPI.Api.Authorization;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Api.Features.Licencas;

public interface ILicencaService
{
    Task<LicencaDto?> GetCurrentAsync(CurrentUserContext currentUser, CancellationToken cancellationToken);

    Task<LicencaDto> GetOrCreateForMedicoAsync(int userId, CancellationToken cancellationToken);

    Task<LicencaDto> UpdateAsync(int userId, UpdateLicencaRequest request, CancellationToken cancellationToken);

    Task<LicencaDto> LiberarCompletaAsync(int userId, LiberarLicencaCompletaRequest request, CancellationToken cancellationToken);

    Task<bool> HasFeatureAsync(CurrentUserContext currentUser, string feature, CancellationToken cancellationToken);
}

public class LicencaService : ILicencaService
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IAppDbContext _context;
    private readonly LicencaOptions _options;

    public LicencaService(IAppDbContext context, IOptions<LicencaOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    public async Task<LicencaDto?> GetCurrentAsync(CurrentUserContext currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.IsAdministrador)
        {
            return CreateUnrestrictedDto(currentUser.Id);
        }

        if (!currentUser.IsMedico)
        {
            return null;
        }

        return await GetOrCreateForMedicoAsync(currentUser.Id, cancellationToken);
    }

    public async Task<LicencaDto> GetOrCreateForMedicoAsync(int userId, CancellationToken cancellationToken)
    {
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, cancellationToken);
        return ToDto(licenca, DateTime.UtcNow);
    }

    public async Task<LicencaDto> UpdateAsync(
        int userId,
        UpdateLicencaRequest request,
        CancellationToken cancellationToken)
    {
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, cancellationToken);

        licenca.Plano = NormalizePlano(request.Plano ?? licenca.Plano);
        licenca.Status = NormalizeStatus(request.Status ?? licenca.Status);

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
            licenca.FeaturesLiberadas = SerializeFeatures(request.FeaturesLiberadas);
        }

        licenca.Observacoes = TrimToNull(request.Observacoes);
        licenca.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(licenca, DateTime.UtcNow);
    }

    public async Task<LicencaDto> LiberarCompletaAsync(
        int userId,
        LiberarLicencaCompletaRequest request,
        CancellationToken cancellationToken)
    {
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, cancellationToken);

        licenca.Plano = LicencaPlanos.Completa;
        licenca.Status = LicencaStatus.Ativa;
        licenca.DataFimLicenca = request.DataFimLicenca?.ToUniversalTime();
        licenca.FeaturesLiberadas = null;
        licenca.Observacoes = TrimToNull(request.Observacoes) ?? licenca.Observacoes;
        licenca.DataAtualizacao = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(licenca, DateTime.UtcNow);
    }

    public async Task<bool> HasFeatureAsync(
        CurrentUserContext currentUser,
        string feature,
        CancellationToken cancellationToken)
    {
        if (currentUser.IsAdministrador)
        {
            return true;
        }

        if (currentUser.IsPaciente)
        {
            return feature is LicencaFeatures.DashboardVisualizar
                or LicencaFeatures.PacientesVisualizar
                or LicencaFeatures.CbhpmConsultar;
        }

        if (!currentUser.IsMedico)
        {
            return false;
        }

        var licenca = await GetOrCreateForMedicoEntityAsync(currentUser.Id, cancellationToken);
        return IsFeatureAllowed(licenca, feature, DateTime.UtcNow);
    }

    private async Task<Licenca> GetOrCreateForMedicoEntityAsync(int userId, CancellationToken cancellationToken)
    {
        var licenca = await _context.Licencas
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (licenca != null)
        {
            return licenca;
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new { item.Id, item.PerfilId })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            throw new KeyNotFoundException("Usuario nao encontrado");
        }

        if (user.PerfilId != Perfil.MedicosId)
        {
            throw new InvalidOperationException("Licenca de uso esta disponivel apenas para medicos");
        }

        var now = DateTime.UtcNow;
        licenca = new Licenca
        {
            UserId = user.Id,
            Plano = LicencaPlanos.Trial,
            Status = LicencaStatus.Ativa,
            DataInicioTrial = now,
            DataFimTrial = now.AddDays(Math.Max(1, _options.TrialDays)),
            DataCadastro = now
        };

        _context.Licencas.Add(licenca);
        await _context.SaveChangesAsync(cancellationToken);

        return licenca;
    }

    private static LicencaDto ToDto(Licenca licenca, DateTime now)
    {
        var featuresLiberadas = ParseFeatures(licenca.FeaturesLiberadas);
        var featuresEfetivas = GetEffectiveFeatures(licenca, now).ToList();
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
            Ativa = IsLicenseActive(licenca, now),
            AcessoCompleto = HasFullAccess(licenca, now),
            DiasRestantesTrial = trialRemainingDays,
            Observacoes = licenca.Observacoes,
            DataCadastro = licenca.DataCadastro,
            DataAtualizacao = licenca.DataAtualizacao
        };
    }

    private static LicencaDto CreateUnrestrictedDto(int userId)
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

    private static bool IsFeatureAllowed(Licenca licenca, string feature, DateTime now)
    {
        return GetEffectiveFeatures(licenca, now).Contains(feature, TextComparer);
    }

    private static IEnumerable<string> GetEffectiveFeatures(Licenca licenca, DateTime now)
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
                .Concat(ParseFeatures(licenca.FeaturesLiberadas))
                .Distinct(TextComparer);
        }

        return ParseFeatures(licenca.FeaturesLiberadas)
            .Distinct(TextComparer);
    }

    private static bool HasFullAccess(Licenca licenca, DateTime now)
    {
        return TextComparer.Equals(licenca.Plano, LicencaPlanos.Completa)
            && IsLicenseActive(licenca, now)
            && (!licenca.DataFimLicenca.HasValue || licenca.DataFimLicenca.Value >= now);
    }

    private static bool IsLicenseActive(Licenca licenca, DateTime now)
    {
        return TextComparer.Equals(licenca.Status, LicencaStatus.Ativa)
            && (!licenca.DataFimLicenca.HasValue || licenca.DataFimLicenca.Value >= now);
    }

    private static string NormalizePlano(string value)
    {
        if (TextComparer.Equals(value, LicencaPlanos.Trial))
        {
            return LicencaPlanos.Trial;
        }

        if (TextComparer.Equals(value, LicencaPlanos.Completa) || TextComparer.Equals(value, "Full"))
        {
            return LicencaPlanos.Completa;
        }

        throw new InvalidOperationException("Plano de licenca invalido");
    }

    private static string NormalizeStatus(string value)
    {
        if (TextComparer.Equals(value, LicencaStatus.Ativa))
        {
            return LicencaStatus.Ativa;
        }

        if (TextComparer.Equals(value, LicencaStatus.Suspensa))
        {
            return LicencaStatus.Suspensa;
        }

        if (TextComparer.Equals(value, LicencaStatus.Cancelada))
        {
            return LicencaStatus.Cancelada;
        }

        throw new InvalidOperationException("Status de licenca invalido");
    }

    private static string? SerializeFeatures(IEnumerable<string> features)
    {
        var normalized = features
            .Select(TrimToNull)
            .Where(feature => feature != null)
            .Select(feature => feature!)
            .Distinct(TextComparer)
            .ToList();

        var invalidFeature = normalized
            .FirstOrDefault(feature => !LicencaFeatures.Todas.Contains(feature, TextComparer));

        if (invalidFeature != null)
        {
            throw new InvalidOperationException($"Feature de licenca invalida: {invalidFeature}");
        }

        return normalized.Count == 0 ? null : string.Join(';', normalized);
    }

    private static IReadOnlyList<string> ParseFeatures(string? features)
    {
        if (string.IsNullOrWhiteSpace(features))
        {
            return [];
        }

        return features
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(TextComparer)
            .ToList();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
