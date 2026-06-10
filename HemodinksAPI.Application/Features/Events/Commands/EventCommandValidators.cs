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
    }
}
