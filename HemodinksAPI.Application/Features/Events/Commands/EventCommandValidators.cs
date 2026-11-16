using FluentValidation;
using HemodinksAPI.Application.Validation;
using HemodinksAPI.Domain.Exceptions;

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
            throw new BusinessRuleException("Evento invalido.");
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
            throw new BusinessRuleException("Informe os dados do evento.");
        }

        var result = new EventPayloadValidator().Validate(request);
        if (!result.IsValid) throw new BusinessRuleException(result.Errors[0].ErrorMessage);
        EventFeatureRules.ValidateNotificationRequest(request);
    }
}

internal sealed class EventPayloadValidator : AbstractValidator<EventRequest>
{
    public EventPayloadValidator()
    {
        Include(new EventScheduleValidator());
        RuleFor(request => request.NotificationUserIds)
            .Must(ids => ids != null && ids.All(id => id > 0)).WithMessage("Informe destinatarios validos.");
        RuleFor(request => request.NotificationGroupIds)
            .Must(ids => ids != null && ids.All(id => id > 0)).WithMessage("Informe destinatarios validos.");
    }
}
