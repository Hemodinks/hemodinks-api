using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Teams;

namespace HemodinksAPI.Api;

public static class EquipeEndpointExtensions
{
    public static void MapEquipeEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/equipes")
            .WithTags("Equipes")
            .RequireAuthorization("Administrador");

        admin.MapGet("/", Listar);
        admin.MapPost("/", Criar);
        admin.MapPut("/{id:int}", Atualizar);
        admin.MapPost("/{id:int}/membros", AssociarMembro);
        admin.MapDelete("/{id:int}/membros/{userId:int}", DesassociarMembro);
        admin.MapPost("/{id:int}/operadores/{operadorId:int}/pin", RedefinirPin);
        admin.MapPut("/{id:int}/operadores/{operadorId:int}/bloqueio", AlterarBloqueio);

        app.MapPost("/api/equipe-auth/identificar", IdentificarOperador)
            .WithName("IdentifyTeamOperator")
            .WithTags("Equipes - Autenticacao")
            .AllowAnonymous()
            .AddEndpointFilter<AuthenticationRateLimitFilter>();

        app.MapPut("/api/equipe-auth/pin", TrocarPin)
            .WithTags("Equipes - Autenticacao")
            .RequireAuthorization("Equipe");
    }

    private static async Task<IResult> Listar(TeamUseCases useCases, CancellationToken cancellationToken) =>
        Results.Ok(await useCases.ListAsync(cancellationToken));

    private static async Task<IResult> Criar(
        CriarEquipeRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = GetCurrentUser(principal);
        var result = await useCases.CreateAsync(currentUser,
            new CreateTeamInput(request.Nome, request.Email, request.Senha, request.Telefone, request.ModoIdentificacao),
            cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.Created($"/api/equipes/{result.Value}", new { Id = result.Value });
    }

    private static async Task<IResult> Atualizar(
        int id,
        AtualizarEquipeRequest request,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var result = await useCases.UpdateAsync(id,
            new UpdateTeamInput(request.Nome, request.ModoIdentificacao, request.Ativa), cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AssociarMembro(
        int id,
        AssociarEquipeMembroRequest request,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var result = await useCases.AssociateMemberAsync(id, request.UserId, request.GerarPin, cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DesassociarMembro(
        int id,
        int userId,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var result = await useCases.RemoveMemberAsync(id, userId, cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RedefinirPin(
        int id,
        int operadorId,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var result = await useCases.ResetPinAsync(id, operadorId, cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.Ok(new { PinTemporario = result.Value });
    }

    private static async Task<IResult> AlterarBloqueio(
        int id,
        int operadorId,
        AlterarBloqueioOperadorRequest request,
        TeamUseCases useCases,
        CancellationToken cancellationToken)
    {
        var result = await useCases.SetOperatorBlockedAsync(id, operadorId, request.Bloqueado, cancellationToken);
        return result.Status == TeamUseCaseStatus.Success ? Results.NoContent() : MapError(result.Status, result.Message);
    }

    private static async Task<IResult> IdentificarOperador(
        IdentificarEquipeRequest request,
        HttpContext httpContext,
        HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService sessions,
        AuthenticationSessionCookie cookie,
        TeamUseCases useCases,
        CancellationToken cancellationToken)
    {
        if (!SessionRequestSecurity.IsTrusted(httpContext, httpContext.RequestServices.GetRequiredService<IConfiguration>(), login: true))
            return Results.StatusCode(403);
        var result = await useCases.IdentifyOperatorAsync(request.Token, request.OperadorId, request.Pin, cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        var issued = await SessionLoginIssuer.StartAsync(result.Value!, httpContext, sessions, cancellationToken);
        if (issued == null) return Results.Unauthorized();
        httpContext.Items[SecurityObservationMiddleware.IssuedClinic] = result.Value!.ClinicaId;
        result.Value!.Token = issued.AccessToken;
        httpContext.Response.Headers.CacheControl = "no-store";
        cookie.Write(httpContext, issued);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> TrocarPin(
        TrocarEquipePinRequest request,
        HemodinksAPI.Application.Features.Sessions.AuthenticationSessionService sessions,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        TeamUseCases useCases,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var currentUser = GetCurrentUser(principal);
        var result = await useCases.ChangePinAsync(currentUser, request.PinAtual, request.NovoPin, cancellationToken);
        if (result.Status != TeamUseCaseStatus.Success) return MapError(result.Status, result.Message);
        if (currentUser.SessionId.HasValue)
        {
            var issuedClaims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(result.Value!.Token).Claims;
            var nextVersion = int.Parse(issuedClaims.Single(c => c.Type == "operadorVersaoSessao").Value);
            var previousVersion = int.Parse(principal.FindFirst("operadorVersaoSessao")!.Value);
            if (!await sessions.AdvanceOperatorVersionAsync(currentUser.SessionId.Value, currentUser.UsuarioClinicaId,
                currentUser.EquipeOperadorId!.Value, previousVersion, nextVersion, cancellationToken)) return Results.Unauthorized();
        }
        httpContext.Response.Headers.CacheControl = "no-store";
        await RecordAuditAsync(result.Audit, httpContext, auditService, cancellationToken);
        return Results.Ok(result.Value);
    }

    private static IResult MapError(TeamUseCaseStatus status, string? message) => status switch
    {
        TeamUseCaseStatus.NotFound => Results.NotFound(),
        TeamUseCaseStatus.BadRequest => Results.BadRequest(new { message }),
        TeamUseCaseStatus.Conflict => Results.Conflict(new { message }),
        TeamUseCaseStatus.Unauthorized => Results.Unauthorized(),
        TeamUseCaseStatus.Forbidden => Results.Forbid(),
        _ => throw new InvalidOperationException("Resultado de equipe invalido.")
    };

    private static Task RecordAuditAsync(
        TeamAudit? audit,
        HttpContext httpContext,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        return audit == null
            ? Task.CompletedTask
            : auditService.RecordAsync(httpContext, audit.Action, audit.Resource, audit.EntityId,
                audit.ClinicId, audit.Details, true, cancellationToken);
    }

    private static CurrentUserContext GetCurrentUser(ClaimsPrincipal principal) =>
        principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
}
