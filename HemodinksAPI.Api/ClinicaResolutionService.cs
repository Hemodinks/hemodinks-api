using System.Security.Claims;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public sealed record ResolvedClinica(int Id, string Nome, string Slug);

public sealed class ClinicaResolutionService
{
    public const string ClinicaIdHeaderName = "X-Clinica-Id";
    public const string ClinicaSlugHeaderName = "X-Clinica-Slug";

    private readonly AppDbContext _context;

    public ClinicaResolutionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ResolvedClinica?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Fail-closed: uma requisicao autenticada nunca troca de clinica por header/subdominio.
            return await ResolveFromAuthenticatedUserAsync(user, cancellationToken);
        }

        var resolvedFromHeader = await ResolveFromHeadersAsync(httpContext.Request, cancellationToken);
        if (resolvedFromHeader != null)
        {
            return resolvedFromHeader;
        }

        var resolvedFromSubdomain = await ResolveFromSubdomainAsync(httpContext.Request.Host.Host, cancellationToken);
        if (resolvedFromSubdomain != null)
        {
            return resolvedFromSubdomain;
        }

        return await ResolveSingleActiveClinicaAsync(cancellationToken);
    }

    private Task<ResolvedClinica?> ResolveFromSubdomainAsync(string? host, CancellationToken cancellationToken)
    {
        var subdomainSlug = TryExtractSubdomainSlug(host);
        return string.IsNullOrWhiteSpace(subdomainSlug)
            ? Task.FromResult<ResolvedClinica?>(null)
            : ResolveBySlugAsync(subdomainSlug, cancellationToken);
    }

    private async Task<ResolvedClinica?> ResolveFromAuthenticatedUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var clinicaIdClaim = user.FindFirst(ClinicaClaimTypes.ClinicaId)?.Value;
        if (int.TryParse(clinicaIdClaim, out var clinicaId) && clinicaId > 0)
        {
            var resolvedByClaim = await ResolveByIdAsync(clinicaId, cancellationToken);
            if (resolvedByClaim != null)
            {
                return resolvedByClaim;
            }
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
        {
            return null;
        }

        return await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == userId && item.Ativo)
            .Select(item => new ResolvedClinica(item.ClinicaId, item.Clinica.Nome, item.Clinica.Slug))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResolvedClinica?> ResolveFromHeadersAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValue(ClinicaSlugHeaderName, out var slugValues))
        {
            var slug = slugValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return await ResolveBySlugAsync(slug, cancellationToken);
            }
        }

        if (request.Headers.TryGetValue(ClinicaIdHeaderName, out var idValues)
            && int.TryParse(idValues.ToString(), out var clinicaId)
            && clinicaId > 0)
        {
            return await ResolveByIdAsync(clinicaId, cancellationToken);
        }

        return null;
    }

    private Task<ResolvedClinica?> ResolveByIdAsync(int clinicaId, CancellationToken cancellationToken)
    {
        return _context.Clinicas
            .AsNoTracking()
            .Where(item => item.Id == clinicaId && item.Ativa)
            .Select(item => new ResolvedClinica(item.Id, item.Nome, item.Slug))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<ResolvedClinica?> ResolveBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();

        return _context.Clinicas
            .AsNoTracking()
            .Where(item => item.Slug == normalizedSlug && item.Ativa)
            .Select(item => new ResolvedClinica(item.Id, item.Nome, item.Slug))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ResolvedClinica?> ResolveSingleActiveClinicaAsync(CancellationToken cancellationToken)
    {
        var activeClinicas = await _context.Clinicas
            .AsNoTracking()
            .Where(item => item.Ativa)
            .OrderBy(item => item.Id)
            .Take(2)
            .Select(item => new ResolvedClinica(item.Id, item.Nome, item.Slug))
            .ToListAsync(cancellationToken);

        return activeClinicas.Count == 1
            ? activeClinicas[0]
            : null;
    }

    private static string? TryExtractSubdomainSlug(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        if (string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || System.Net.IPAddress.TryParse(normalizedHost, out _))
        {
            return null;
        }

        var hostWithoutApiPrefix = normalizedHost.StartsWith("api.", StringComparison.Ordinal)
            ? normalizedHost["api.".Length..]
            : normalizedHost;

        var segments = hostWithoutApiPrefix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length >= 3)
        {
            return segments[0];
        }

        if (segments.Length == 2 && string.Equals(segments[1], "localhost", StringComparison.Ordinal))
        {
            return segments[0];
        }

        return null;
    }
}
