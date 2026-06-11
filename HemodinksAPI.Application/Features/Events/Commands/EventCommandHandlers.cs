using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events.Commands;

public sealed class EventCommandHandler :
    IRequestHandler<CreateEventCommand, EventDto>,
    IRequestHandler<UpdateEventCommand, EventDto>,
    IRequestHandler<CompleteEventCommand>,
    IRequestHandler<DeleteEventCommand>
{
    private readonly IAppDbContext _context;

    public EventCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<EventDto> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var ownerUserId = await ResolveOwnerUserIdAsync(
            request.Request.UserId,
            request.CurrentUser,
            cancellationToken);

        var medicalUserId = await ResolveMedicalUserIdAsync(
            request.Request,
            request.CurrentUser,
            cancellationToken);

        var ev = EventFeatureRules.ApplyRequest(
            new Event(),
            request.Request,
            ownerUserId,
            medicalUserId,
            isCreate: true);

        _context.Events.Add(ev);
        await _context.SaveChangesAsync(cancellationToken);

        return await FindEventDtoAsync(ev.Id, cancellationToken);
    }

    public async Task<EventDto> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (ev == null)
        {
            throw new KeyNotFoundException();
        }

        EventFeatureRules.EnsureCanManageEvent(ev, request.CurrentUser);

        var ownerUserId = await ResolveOwnerUserIdAsync(
            request.Request.UserId ?? ev.UserId,
            request.CurrentUser,
            cancellationToken);

        var medicalUserId = await ResolveMedicalUserIdAsync(
            request.Request,
            request.CurrentUser,
            cancellationToken);

        EventFeatureRules.ApplyRequest(
            ev,
            request.Request,
            ownerUserId,
            medicalUserId,
            isCreate: false);

        await _context.SaveChangesAsync(cancellationToken);

        return await FindEventDtoAsync(request.Id, cancellationToken);
    }

    public async Task Handle(CompleteEventCommand request, CancellationToken cancellationToken)
    {
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (ev == null)
        {
            throw new KeyNotFoundException();
        }

        EventFeatureRules.EnsureCanManageEvent(ev, request.CurrentUser);

        ev.IsCompleted = true;
        ev.CompletedAt = DateTime.UtcNow;
        ev.NextReminderAt = null;
        ev.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (ev == null)
        {
            throw new KeyNotFoundException();
        }

        EventFeatureRules.EnsureCanManageEvent(ev, request.CurrentUser);

        _context.Events.Remove(ev);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<EventDto> FindEventDtoAsync(int eventId, CancellationToken cancellationToken)
    {
        var ev = await _context.Events
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.MedicalUser)
            .Where(item => item.Id == eventId)
            .FirstAsync(cancellationToken);

        return EventFeatureRules.ToDto(ev);
    }

    private async Task<int> ResolveOwnerUserIdAsync(
        int? requestedUserId,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var ownerUserId = requestedUserId ?? currentUser.Id;

        if (!currentUser.IsAdministrador && ownerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
        }

        var ownerExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == ownerUserId && user.Ativo, cancellationToken);

        if (!ownerExists)
        {
            throw new InvalidOperationException("Usuario responsavel pelo evento nao encontrado ou inativo.");
        }

        return ownerUserId;
    }

    private async Task<int?> ResolveMedicalUserIdAsync(
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

        var isValidMedicalUser = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == medicalUserId.Value
                && user.Ativo
                && user.PerfilId == Perfil.MedicosId, cancellationToken);

        if (!isValidMedicalUser)
        {
            throw new InvalidOperationException("Medico selecionado para notificacao nao encontrado ou inativo.");
        }

        return medicalUserId.Value;
    }
}
