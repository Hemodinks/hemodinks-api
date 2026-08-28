using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Application.Features.Licencas;

public class LicencaService : ILicencaService
{
    private readonly ILicensingFeatureDbContext _context;
    private readonly LicencaOptions _options;

    public LicencaService(ILicensingFeatureDbContext context, IOptions<LicencaOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    public async Task<LicencaDto?> GetCurrentAsync(CurrentUserContext currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.IsAdministrador || currentUser.IsController || currentUser.IsEquipe)
        {
            return LicencaMapper.CreateUnrestrictedDto(currentUser.Id);
        }

        if (!currentUser.IsMedico)
        {
            return null;
        }

        return await GetOrCreateForMedicoAsync(currentUser.Id, cancellationToken);
    }

    public async Task<LicencaDto> GetOrCreateForMedicoAsync(int userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, now, cancellationToken);
        return LicencaMapper.ToDto(licenca, now);
    }

    public async Task<LicencaDto> UpdateAsync(
        int userId,
        UpdateLicencaRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, now, cancellationToken);

        LicencaMutations.ApplyUpdate(licenca, request, now);
        await _context.SaveChangesAsync(cancellationToken);

        return LicencaMapper.ToDto(licenca, now);
    }

    public async Task<LicencaDto> LiberarCompletaAsync(
        int userId,
        LiberarLicencaCompletaRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var licenca = await GetOrCreateForMedicoEntityAsync(userId, now, cancellationToken);

        LicencaMutations.ApplyFullRelease(licenca, request, now);
        await _context.SaveChangesAsync(cancellationToken);

        return LicencaMapper.ToDto(licenca, now);
    }

    public async Task<bool> HasFeatureAsync(
        CurrentUserContext currentUser,
        string feature,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdministrador
            && !await IsClinicaSubscriptionActiveAsync(currentUser.ClinicaId, cancellationToken))
        {
            return false;
        }

        if (LicencaFeatureRules.HasImplicitFeatureAccess(currentUser, feature, out var allowed))
        {
            return allowed;
        }

        var licenca = await GetOrCreateForMedicoEntityAsync(currentUser.Id, DateTime.UtcNow, cancellationToken);
        return LicencaFeatureRules.IsFeatureAllowed(licenca, feature, DateTime.UtcNow);
    }

    private async Task<bool> IsClinicaSubscriptionActiveAsync(int clinicaId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await _context.Clinicas
            .AsNoTracking()
            .AnyAsync(clinica => clinica.Id == clinicaId
                && clinica.Ativa
                && (clinica.AssinaturaStatus == ClinicaAssinaturaStatus.Ativa
                    && (!clinica.AssinaturaValidaAte.HasValue || clinica.AssinaturaValidaAte >= now)
                    || clinica.AssinaturaStatus == ClinicaAssinaturaStatus.Trial
                    && (!clinica.TrialAte.HasValue || clinica.TrialAte >= now)),
                cancellationToken);
    }

    private async Task<Licenca> GetOrCreateForMedicoEntityAsync(
        int userId,
        DateTime now,
        CancellationToken cancellationToken)
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
            .Select(item => new { item.Id, item.PerfilId, item.ClinicaId })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            throw new KeyNotFoundException("Usuario nao encontrado");
        }

        if (user.PerfilId != Perfil.MedicosId)
        {
            throw new InvalidOperationException("Licenca de uso esta disponivel apenas para medicos");
        }

        licenca = LicencaMutations.CreateTrial(user.Id, user.ClinicaId, now, _options.TrialDays);
        _context.Licencas.Add(licenca);
        await _context.SaveChangesAsync(cancellationToken);

        return licenca;
    }
}
