using HemodinksAPI.Api.Authorization;
using MediatR;

namespace HemodinksAPI.Api.Features.Events.Commands;

public sealed class CreateEventCommand : IRequest<EventDto>
{
    public CurrentUserContext CurrentUser { get; set; } = null!;

    public EventRequest Request { get; set; } = null!;
}

public sealed class UpdateEventCommand : IRequest<EventDto>
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;

    public EventRequest Request { get; set; } = null!;
}

public sealed class CompleteEventCommand : IRequest
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}

public sealed class DeleteEventCommand : IRequest
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}
