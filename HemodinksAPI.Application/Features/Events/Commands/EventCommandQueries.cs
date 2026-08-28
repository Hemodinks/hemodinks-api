using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events.Commands;

internal static class EventCommandQueries
{
    public static async Task<EventDto> FindEventDtoAsync(
        IEventFeatureDbContext context,
        int eventId,
        CancellationToken cancellationToken)
    {
        var ev = await context.Events
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.MedicalUser)
            .Where(item => item.Id == eventId)
            .FirstAsync(cancellationToken);

        return EventFeatureRules.ToDto(ev);
    }

    public static async Task<int> ResolveOwnerUserIdAsync(
        IEventFeatureDbContext context,
        int? requestedUserId,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var ownerUserId = requestedUserId ?? currentUser.Id;

        if (!currentUser.IsAdministrador && ownerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
        }

        var ownerExists = await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == ownerUserId && user.Ativo, cancellationToken);

        if (!ownerExists)
        {
            throw new InvalidOperationException("Usuario responsavel pelo evento nao encontrado ou inativo.");
        }

        return ownerUserId;
    }

    public static async Task<int?> ResolveMedicalUserIdAsync(
        IEventFeatureDbContext context,
        EventRequest request,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var medicalUserId = request.MedicalUserId;

        if (request.NotifyMedicalProfile && !medicalUserId.HasValue && currentUser.IsMedico)
        {
            medicalUserId = currentUser.Id;
        }

        if (!medicalUserId.HasValue)
        {
            return null;
        }

        var isValidMedicalUser = await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == medicalUserId.Value
                && user.Ativo
                && user.PerfilId == Perfil.MedicosId, cancellationToken);

        if (isValidMedicalUser && currentUser.IsEquipe)
        {
            isValidMedicalUser = currentUser.EquipeId.HasValue
                && await context.EquipeMembros
                    .AsNoTracking()
                    .AnyAsync(member => member.EquipeId == currentUser.EquipeId.Value
                        && member.UserId == medicalUserId.Value
                        && member.Ativo,
                        cancellationToken);
        }

        if (!isValidMedicalUser)
        {
            throw new InvalidOperationException("Medico selecionado para notificacao nao encontrado ou inativo.");
        }

        return medicalUserId.Value;
    }
}
