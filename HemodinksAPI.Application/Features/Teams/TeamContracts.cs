using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Teams;

public enum TeamUseCaseStatus { Success, NotFound, BadRequest, Conflict, Unauthorized, Forbidden }
public record TeamUseCaseResult(TeamUseCaseStatus Status, string? Message = null, TeamAudit? Audit = null)
{
    public static TeamUseCaseResult Success(TeamAudit? audit = null) => new(TeamUseCaseStatus.Success, Audit: audit);
    public static TeamUseCaseResult NotFound() => new(TeamUseCaseStatus.NotFound);
}
public sealed record TeamUseCaseResult<T>(TeamUseCaseStatus Status, T Value = default!, string? Message = null, TeamAudit? Audit = null)
{
    public static TeamUseCaseResult<T> Success(T value, TeamAudit? audit = null) => new(TeamUseCaseStatus.Success, value, Audit: audit);
    public static TeamUseCaseResult<T> NotFound() => new(TeamUseCaseStatus.NotFound);
    public static TeamUseCaseResult<T> BadRequest(string message) => new(TeamUseCaseStatus.BadRequest, Message: message);
    public static TeamUseCaseResult<T> Conflict(string message) => new(TeamUseCaseStatus.Conflict, Message: message);
    public static TeamUseCaseResult<T> Unauthorized() => new(TeamUseCaseStatus.Unauthorized);
    public static TeamUseCaseResult<T> Forbidden() => new(TeamUseCaseStatus.Forbidden);
}
public sealed record TeamAudit(string Action, string Resource, string EntityId, int ClinicId, object Details)
{
    public static TeamAudit Create(string action, string resource, int entityId, int clinicId, object details) =>
        new(action, resource, entityId.ToString(), clinicId, details);
}
public sealed record CreateTeamInput(string Name, string Email, string Password, string? Phone, string? IdentificationMode);
public sealed record UpdateTeamInput(string? Name, string? IdentificationMode, bool? Active);
public sealed record AssociateTeamMemberResponse(int Id, string? PinTemporario);
public sealed record ChangeTeamPinResponse(string Token, bool PrecisaTrocarPin);
public sealed record TeamResponse(int Id, string Nome, int UsuarioLoginId, string Email, string ModoIdentificacao,
    bool Ativa, IReadOnlyList<TeamMemberResponse> Membros);
public sealed record TeamMemberResponse(int UserId, string Nome, string Email, int PerfilId, int OperadorId,
    bool OperadorAtivo, bool PossuiPin, bool PrecisaTrocarPin, DateTime? BloqueadoAte);
