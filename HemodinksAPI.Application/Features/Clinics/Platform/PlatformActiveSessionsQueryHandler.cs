using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

/// <summary>
/// Leitura das sessoes ativas de todas as clinicas. Exclusivo do SuperAdministrador
/// (politica aplicada no endpoint) e resolvido sobre o PlatformDbContext.
/// Nao expoe hash de refresh token, IP, e-mail nem CPF.
/// </summary>
public sealed class PlatformActiveSessionsQueryHandler(
    IPlatformSessionMonitorDbContext context,
    AuthenticationSessionOptions options,
    SessionLifetimePolicy lifetime)
{
    public async Task<PlatformUseCaseResult> ListActiveSessions(
        int pagina = 1,
        int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 200);

        var now = lifetime.UtcNow;
        var query = ActiveSessions(now);

        var totalSessoes = await query.CountAsync(cancellationToken);
        var usuariosOnline = await query
            .Select(session => session.UsuarioGlobalId)
            .Distinct()
            .CountAsync(cancellationToken);
        var porClinica = await query
            .GroupBy(session => new { session.UsuarioClinica.ClinicaId, session.UsuarioClinica.Clinica.Nome })
            .Select(group => new
            {
                clinicaId = group.Key.ClinicaId,
                clinica = group.Key.Nome,
                usuarios = group.Select(session => session.UsuarioGlobalId).Distinct().Count(),
                sessoes = group.Count()
            })
            .OrderByDescending(item => item.usuarios)
            .ThenBy(item => item.clinica)
            .ToListAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(session => session.LastActivityAt)
            .ThenBy(session => session.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(session => new
            {
                session.UsuarioGlobalId,
                Nome = session.UsuarioClinica.UsuarioGlobal.Nome,
                // Fonte canonica do perfil e o User local (ver AuthenticationSessionService).
                Perfil = session.UsuarioClinica.User.Perfil.Nome,
                session.UsuarioClinica.ClinicaId,
                Clinica = session.UsuarioClinica.Clinica.Nome,
                ViaEquipe = session.EquipeId != null,
                session.CreatedAt,
                session.LastActivityAt,
                session.UserAgent
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new
        {
            usuarioGlobalId = row.UsuarioGlobalId,
            nome = row.Nome,
            perfil = row.Perfil,
            clinicaId = row.ClinicaId,
            clinica = row.Clinica,
            viaEquipe = row.ViaEquipe,
            inicio = row.CreatedAt,
            ultimaAtividade = row.LastActivityAt,
            navegador = UserAgentSummary.Describe(row.UserAgent)
        }).ToList();

        return PlatformUseCaseResult.Ok(new
        {
            geradoEm = now,
            janelaOciosidadeMinutos = options.IdleTimeoutMinutes,
            usuariosOnline,
            totalSessoes,
            porClinica,
            pagina,
            tamanhoPagina,
            items
        });
    }

    /// <summary>
    /// Resumo so com contagens (sem nome, perfil, nome de clinica ou navegador), para telemetria
    /// fora da aplicacao. Mesmo criterio de sessao ativa da listagem.
    /// </summary>
    public async Task<ActiveSessionsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var query = ActiveSessions(lifetime.UtcNow);

        var totalSessoes = await query.CountAsync(cancellationToken);
        var usuariosOnline = await query
            .Select(session => session.UsuarioGlobalId)
            .Distinct()
            .CountAsync(cancellationToken);
        var porClinica = await query
            .GroupBy(session => session.UsuarioClinica.ClinicaId)
            .Select(group => new ActiveSessionsClinicCount(
                group.Key,
                group.Select(session => session.UsuarioGlobalId).Distinct().Count(),
                group.Count()))
            .ToListAsync(cancellationToken);

        return new ActiveSessionsSnapshot(
            usuariosOnline,
            totalSessoes,
            porClinica.OrderBy(item => item.ClinicaId).ToList());
    }

    private IQueryable<AuthenticationSession> ActiveSessions(DateTime now)
    {
        // Mesmos limites de SessionLifetimePolicy.Failure: ociosidade e tempo absoluto.
        var idleCutoff = now
            .AddMinutes(-options.IdleTimeoutMinutes)
            .AddSeconds(-options.ActivityPersistenceIntervalSeconds);
        var absoluteCutoff = now.AddHours(-options.AbsoluteLifetimeHours);

        // Mesmas condicoes de AuthenticationSessionService.IsActive/IsActive(membership) e, para equipe,
        // o nucleo de FindTeamBindingAsync (equipe ativa, da clinica, do login e na versao de sessao).
        // Invalidacao em nivel de operador (bloqueio, PIN) so aparece aqui depois da proxima validacao.
        return context.AuthenticationSessions.AsNoTracking()
            .Where(session => session.RevokedAt == null
                && session.LastActivityAt > idleCutoff
                && session.CreatedAt > absoluteCutoff
                && session.CreatedAt <= now
                && session.UsuarioGlobalId == session.UsuarioClinica.UsuarioGlobalId
                && session.SecurityVersion == session.UsuarioClinica.UsuarioGlobal.SecurityVersion
                && session.UsuarioClinica.Ativo
                && session.UsuarioClinica.UsuarioGlobal.Ativo
                && session.UsuarioClinica.User.Ativo
                && session.UsuarioClinica.User.ClinicaId == session.UsuarioClinica.ClinicaId
                && session.UsuarioClinica.Clinica.Ativa
                && (session.EquipeId == null
                    ? session.UsuarioClinica.User.PerfilId != Perfil.EquipeId
                    : session.UsuarioClinica.User.PerfilId == Perfil.EquipeId
                        && context.Equipes.Any(team => team.Id == session.EquipeId
                            && team.ClinicaId == session.UsuarioClinica.ClinicaId
                            && team.UsuarioLoginId == session.UsuarioClinica.UserId
                            && team.Ativa
                            && team.VersaoSessao == session.EquipeVersaoSessao)));
    }
}

public sealed record ActiveSessionsSnapshot(
    int UsuariosOnline,
    int TotalSessoes,
    IReadOnlyList<ActiveSessionsClinicCount> PorClinica);

public sealed record ActiveSessionsClinicCount(int ClinicaId, int Usuarios, int Sessoes);

/// <summary>Resumo grosseiro do User-Agent (navegador e sistema), sem versoes nem identificadores.</summary>
public static class UserAgentSummary
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Desconhecido";

        var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Outro";

        var system = userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "Outro";

        return $"{browser} · {system}";
    }
}
