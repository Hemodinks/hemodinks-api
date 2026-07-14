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
    private readonly IAppDbContext _context;
    private readonly IClinicaContext _clinicaContext;

    public EventCommandHandler(IAppDbContext context, IClinicaContext clinicaContext)
    {
        _context = context;
        _clinicaContext = clinicaContext;
    }

    public async Task<EventDto> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var clinicaId = _clinicaContext.GetRequiredClinicaId();
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

        _context.Events.Add(ev);
        EventNotificationMutations.AddAgendaNotifications(_context, ev, request.CurrentUser, request.Request, clinicaId);
        await _context.SaveChangesAsync(cancellationToken);

        return await EventCommandQueries.FindEventDtoAsync(_context, ev.Id, cancellationToken);
    }

    public async Task<EventDto> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var ev = await _context.Events.FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
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

}
