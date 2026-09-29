using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
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
    private readonly IEventFeatureDbContext _context;
    private readonly IClinicaContext _clinicaContext;

    public EventCommandHandler(IEventFeatureDbContext context, IClinicaContext clinicaContext)
    {
        _context = context;
        _clinicaContext = clinicaContext;
    }

    public async Task<EventDto> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var clinicaId = RequireClinica(request.CurrentUser);
        EventFeatureRules.ValidateNotificationRequest(request.Request);

        var ownerUserId = await EventCommandQueries.ResolveOwnerUserIdAsync(
            _context,
            request.Request.UserId,
            request.CurrentUser,
            cancellationToken);

        var medicalUserId = await EventCommandQueries.ResolveMedicalUserIdAsync(
            _context,
            request.Request,
            request.CurrentUser,
            cancellationToken);

        var ev = EventFeatureRules.ApplyRequest(
            new Event(),
            request.Request,
            ownerUserId,
            medicalUserId,
            isCreate: true);
        ev.ClinicaId = clinicaId;

        EventNotificationMutations.AddAgendaNotifications(_context, ev, request.CurrentUser, request.Request, clinicaId);
        _context.Events.Add(ev);
        await _context.SaveChangesAsync(cancellationToken);

        return await EventCommandQueries.FindEventDtoAsync(_context, ev.Id, cancellationToken);
    }

    public async Task<EventDto> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var clinicaId = RequireClinica(request.CurrentUser);
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id && item.ClinicaId == clinicaId, cancellationToken);
        if (ev == null)
        {
            throw new KeyNotFoundException();
        }

        EventFeatureRules.EnsureCanManageEvent(ev, request.CurrentUser);

        var ownerUserId = await EventCommandQueries.ResolveOwnerUserIdAsync(
            _context,
            request.Request.UserId ?? ev.UserId,
            request.CurrentUser,
            cancellationToken);

        var medicalUserId = await EventCommandQueries.ResolveMedicalUserIdAsync(
            _context,
            request.Request,
            request.CurrentUser,
            cancellationToken);

        EventFeatureRules.ResolveNotificationRecipientUserIds(_context, request.CurrentUser, request.Request);

        EventFeatureRules.ApplyRequest(
            ev,
            request.Request,
            ownerUserId,
            medicalUserId,
            isCreate: false);

        await _context.SaveChangesAsync(cancellationToken);

        return await EventCommandQueries.FindEventDtoAsync(_context, request.Id, cancellationToken);
    }

    public async Task Handle(CompleteEventCommand request, CancellationToken cancellationToken)
    {
        var clinicaId = RequireClinica(request.CurrentUser);
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id && item.ClinicaId == clinicaId, cancellationToken);
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
        var clinicaId = RequireClinica(request.CurrentUser);
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id && item.ClinicaId == clinicaId, cancellationToken);
        if (ev == null)
        {
            throw new KeyNotFoundException();
        }

        EventFeatureRules.EnsureCanManageEvent(ev, request.CurrentUser);

        _context.Events.Remove(ev);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private int RequireClinica(HemodinksAPI.Application.Authorization.CurrentUserContext actor)
    {
        var clinicaId = _clinicaContext.GetRequiredClinicaId();
        if (actor.ClinicaId != clinicaId) throw new UnauthorizedAccessException();
        return clinicaId;
    }
}
