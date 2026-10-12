using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Events;

public sealed record EventEmailRecipientDto(int UserId, string Nome, string Email, string Status);
public sealed record EventEmailNotificationDto(
    IReadOnlyList<EventEmailRecipientDto> Recipients, int RecipientCount, int PendingCount,
    int SentCount, int FailedCount, int ExcludedCount, bool CanSend);
public sealed record QueueEventEmailResult(int QueuedCount);
public sealed record GetEventEmailNotificationQuery(int Id, CurrentUserContext CurrentUser) : IRequest<EventEmailNotificationDto>;
public sealed record QueueEventEmailNotificationCommand(int Id, CurrentUserContext CurrentUser) : IRequest<QueueEventEmailResult>;

