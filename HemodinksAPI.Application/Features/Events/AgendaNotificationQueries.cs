using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Events;

public sealed class GetAgendaNotificationRecipientOptionsQuery : IRequest<AgendaNotificationRecipientOptionsDto>
{
    public string? Search { get; set; }
    public string? Profile { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public CurrentUserContext CurrentUser { get; set; } = null!;
}
