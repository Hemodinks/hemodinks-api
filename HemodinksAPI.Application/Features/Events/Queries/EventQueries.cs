using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Events.Queries;

public sealed class GetEventMedicalUsersQuery : IRequest<IReadOnlyList<EventMedicalUserDto>>
{
    public CurrentUserContext CurrentUser { get; set; } = null!;
}

public sealed class GetEventsQuery : IRequest<IReadOnlyList<EventDto>>
{
    public string? Search { get; set; }

    public int? UserId { get; set; }

    public bool? IsCompleted { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}

public sealed class GetEventByIdQuery : IRequest<EventDto?>
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}
