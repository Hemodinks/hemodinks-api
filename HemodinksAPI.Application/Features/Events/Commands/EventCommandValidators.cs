using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.Events.Commands;

public sealed class CreateEventCommandValidator : IRequestValidator<CreateEventCommand>
{
    public void Validate(CreateEventCommand request)
    {
        EventRequestValidator.Validate(request.Request);
    }
}

public sealed class UpdateEventCommandValidator : IRequestValidator<UpdateEventCommand>
{
    public void Validate(UpdateEventCommand request)
    {
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Evento invalido.");
        }

        EventRequestValidator.Validate(request.Request);
    }
}

internal static class EventRequestValidator
{
    public static void Validate(EventRequest? request)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Informe os dados do evento.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new InvalidOperationException("Informe o titulo do evento.");
        }

        if (request.End <= request.Start)
        {
            throw new InvalidOperationException("A data final do evento deve ser maior que a data inicial.");
        }

        if (request.NotificationMessage is { Length: > 500 })
        {
            throw new InvalidOperationException("A mensagem da notificacao deve ter no maximo 500 caracteres.");
        }

        var hasRecipients = request.NotifyAllAllowedRecipients
            || request.NotificationUserIds.Count > 0
            || request.NotificationGroupIds.Count > 0;

        if (hasRecipients && string.IsNullOrWhiteSpace(request.NotificationMessage))
        {
            throw new InvalidOperationException("Informe a mensagem da notificacao.");
        }

        if (!hasRecipients && !string.IsNullOrWhiteSpace(request.NotificationMessage))
        {
            throw new InvalidOperationException("Selecione ao menos um destinatario para enviar a notificacao.");
        }
    }
}
