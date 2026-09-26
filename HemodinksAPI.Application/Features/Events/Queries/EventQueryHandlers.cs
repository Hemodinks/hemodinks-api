using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events.Queries;

public sealed class GetEventMedicalUsersQueryHandler
    : IRequestHandler<GetEventMedicalUsersQuery, IReadOnlyList<EventMedicalUserDto>>
{
    private readonly IEventFeatureDbContext _context;

    public GetEventMedicalUsersQueryHandler(IEventFeatureDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EventMedicalUserDto>> Handle(
        GetEventMedicalUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CurrentUser.IsPaciente || (request.CurrentUser.IsEquipe && !request.CurrentUser.EquipeId.HasValue))
            throw new UnauthorizedAccessException();
        var query = EventRecipientScope.MedicalUsers(_context, request.CurrentUser);

        return await query
            .OrderBy(user => user.Nome)
            .Select(user => new EventMedicalUserDto
            {
                Id = user.Id,
                Nome = user.Nome
            })
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetEventsQueryHandler
    : IRequestHandler<GetEventsQuery, IReadOnlyList<EventDto>>,
      IRequestHandler<GetEventByIdQuery, EventDto?>
{
    private readonly IEventFeatureDbContext _context;
    private readonly IEventReminderProcessor _reminderProcessor;

    public GetEventsQueryHandler(IEventFeatureDbContext context, IEventReminderProcessor reminderProcessor)
    {
        _context = context;
        _reminderProcessor = reminderProcessor;
    }

    public async Task<IReadOnlyList<EventDto>> Handle(GetEventsQuery request, CancellationToken cancellationToken)
    {
        await _reminderProcessor.ProcessDueRemindersAsync(cancellationToken);

        var query = EventFeatureRules.ApplyScope(_context, _context.Events.AsNoTracking(), request.CurrentUser);

        if (request.From.HasValue)
        {
            var fromUtc = EventFeatureRules.ToUtc(request.From.Value);
            query = query.Where(ev => ev.End >= fromUtc);
        }

        if (request.To.HasValue)
        {
            var toUtc = EventFeatureRules.ToUtc(request.To.Value);
            query = query.Where(ev => ev.Start <= toUtc);
        }

        var events = await query
            .Include(ev => ev.User)
            .Include(ev => ev.MedicalUser)
            .OrderBy(ev => ev.Start)
            .ThenBy(ev => ev.Title)
            .ToListAsync(cancellationToken);

        return events.Select(EventFeatureRules.ToDto).ToList();
    }

    public async Task<EventDto?> Handle(GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        var ev = await EventFeatureRules.ApplyScope(_context, _context.Events.AsNoTracking(), request.CurrentUser)
            .Include(item => item.User)
            .Include(item => item.MedicalUser)
            .Where(item => item.Id == request.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return ev == null ? null : EventFeatureRules.ToDto(ev);
    }
}
