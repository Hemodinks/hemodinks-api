using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Events;

public sealed class GetAgendaNotificationRecipientOptionsQuery : IRequest<AgendaNotificationRecipientOptionsDto>
{
    public CurrentUserContext CurrentUser { get; set; } = null!;
}
