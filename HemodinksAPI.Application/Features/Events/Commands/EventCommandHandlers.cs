using HemodinksAPI.Application.Authorization;
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
        ev.ClinicaId = clinicaId;

        _context.Events.Add(ev);
        AddAgendaNotifications(ev, request.CurrentUser, request.Request, clinicaId);
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

    private void AddAgendaNotifications(Event ev, CurrentUserContext currentUser, EventRequest request)
    {
        AddAgendaNotifications(ev, currentUser, request, _clinicaContext.GetRequiredClinicaId());
    }

    private void AddAgendaNotifications(Event ev, CurrentUserContext currentUser, EventRequest request, int clinicaId)
    {
        var recipientUserIds = EventFeatureRules.ResolveNotificationRecipientUserIds(_context, currentUser, request);
        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var title = ev.Title.Trim();
        var message = string.IsNullOrWhiteSpace(request.NotificationMessage)
            ? (ev.Description?.Trim() ?? title)
            : request.NotificationMessage.Trim();

        foreach (var recipientUserId in recipientUserIds)
        {
            _context.AgendaNotifications.Add(new AgendaNotification
            {
                ClinicaId = clinicaId,
                Event = ev,
                SenderUserId = currentUser.Id,
                RecipientUserId = recipientUserId,
                Title = title,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
