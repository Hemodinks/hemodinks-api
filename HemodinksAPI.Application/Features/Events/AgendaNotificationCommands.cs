using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Events;

public sealed class MarkAgendaNotificationsAsReadCommand : IRequest<int>
{
    public CurrentUserContext CurrentUser { get; set; } = null!;
}
