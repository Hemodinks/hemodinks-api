using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public sealed class MarkAgendaNotificationsAsReadCommandHandler : IRequestHandler<MarkAgendaNotificationsAsReadCommand, int>
{
    private readonly IEventFeatureDbContext _context;

    public MarkAgendaNotificationsAsReadCommandHandler(IEventFeatureDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(MarkAgendaNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var unreadNotifications = await _context.AgendaNotifications
            .Where(notification =>
                notification.RecipientUserId == request.CurrentUser.Id
                && notification.ReadAt == null)
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unreadNotifications)
        {
            notification.ReadAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return unreadNotifications.Count;
    }
}
