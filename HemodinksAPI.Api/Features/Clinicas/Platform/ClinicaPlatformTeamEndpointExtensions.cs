using HemodinksAPI.Application.Features.Clinics.Platform;
using HemodinksAPI.Application.Features.Teams;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    private static Task<IResult> ListClinicTeams(
        int id,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.ListClinicTeams(id, cancellationToken).ToHttpResultAsync();

    private static Task<IResult> ListClinicTeamUsers(
        int id,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.ListClinicTeamUsers(id, cancellationToken).ToHttpResultAsync();

    private static Task<IResult> UpdateClinicTeam(
        int id,
        int teamId,
        AtualizarEquipeRequest request,
        HttpContext httpContext,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.UpdateClinicTeam(id, teamId, request, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> AddClinicTeamMember(
        int id,
        int teamId,
        AssociateClinicTeamMembersRequest request,
        HttpContext httpContext,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.AddClinicTeamMember(id, teamId, request, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> RemoveClinicTeamMember(
        int id,
        int teamId,
        int userId,
        HttpContext httpContext,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.RemoveClinicTeamMember(id, teamId, userId, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();

    private static Task<IResult> ResetClinicTeamOperatorPin(
        int id,
        int teamId,
        int operatorId,
        HttpContext httpContext,
        ClinicaPlatformTeamRequestHandler handler,
        CancellationToken cancellationToken) =>
        handler.ResetClinicTeamOperatorPin(id, teamId, operatorId, httpContext.ToPlatformRequestContext(), cancellationToken).ToHttpResultAsync();
}
