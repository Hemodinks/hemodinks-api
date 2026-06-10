using HemodinksAPI.Api.Authorization;
using MediatR;

namespace HemodinksAPI.Api.Features.Events.Queries;

public sealed class GetEventMedicalUsersQuery : IRequest<IReadOnlyList<EventMedicalUserDto>>
{
}

public sealed class GetEventsQuery : IRequest<IReadOnlyList<EventDto>>
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}

public sealed class GetEventByIdQuery : IRequest<EventDto?>
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}
