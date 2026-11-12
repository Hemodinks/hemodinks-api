using System.IdentityModel.Tokens.Jwt;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Features.Users.Commands;

namespace HemodinksAPI.Api;

internal static class SessionLoginIssuer
{
    // The token here is produced by the successful login use case, never supplied by the request.
    internal static Task<IssuedAuthenticationSession?> StartAsync(AuthenticateUserResponse result,
        HttpContext context, AuthenticationSessionService sessions, CancellationToken ct)
    {
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(result.Token).Claims.ToDictionary(c => c.Type, c => c.Value);
        TeamSessionIdentity? team = null;
        if (claims.TryGetValue("equipeId", out var teamId))
            team = new TeamSessionIdentity(int.Parse(teamId),
                claims.TryGetValue("equipeOperadorId", out var op) ? int.Parse(op) : null,
                int.Parse(claims["equipeVersaoSessao"]),
                claims.TryGetValue("operadorVersaoSessao", out var version) ? int.Parse(version) : null,
                claims.GetValueOrDefault("identificacaoConfiavel") == "true",
                SessionLifetimePolicy.ParseAuthenticationTime(claims.GetValueOrDefault("auth_time"))
                    ?? throw new InvalidOperationException("Login did not provide original authentication time."));
        return sessions.StartAsync(result.UsuarioGlobalId, result.Id, result.ClinicaId,
            context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), ct,
            Guid.Parse(claims["security_version"]), team);
    }
}
