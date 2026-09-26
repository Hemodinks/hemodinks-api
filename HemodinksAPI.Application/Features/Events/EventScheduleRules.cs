using FluentValidation;

namespace HemodinksAPI.Application.Features.Events;

internal static class EventScheduleRules
{
    public const string TitleRequired = "Título é obrigatório.";
    public const string InvalidStart = "Informe uma data e um horário de início válidos.";
    public const string InvalidEnd = "Informe uma data e um horário de término válidos.";
    public const string EndAfterStart = "O término deve ser posterior ao início.";

    public static bool IsValid(DateTime value) => value != default
        && (value.Kind == DateTimeKind.Utc || !TimeZoneInfo.Local.IsInvalidTime(DateTime.SpecifyKind(value, DateTimeKind.Unspecified)));

    // Preserve the existing interpretation of timestamps without an offset as server-local time.
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };

    public static void Validate(EventRequest request)
    {
        var result = new EventScheduleValidator().Validate(request);
        if (!result.IsValid) throw new InvalidOperationException(result.Errors[0].ErrorMessage);
    }
}

internal sealed class EventScheduleValidator : AbstractValidator<EventRequest>
{
    public EventScheduleValidator()
    {
        RuleFor(request => request.Title).NotEmpty().WithMessage(EventScheduleRules.TitleRequired);
        RuleFor(request => request.Start).Must(EventScheduleRules.IsValid).WithMessage(EventScheduleRules.InvalidStart);
        RuleFor(request => request.End).Must(EventScheduleRules.IsValid).WithMessage(EventScheduleRules.InvalidEnd);
        RuleFor(request => request.End)
            .Must((request, end) => EventScheduleRules.ToUtc(end) > EventScheduleRules.ToUtc(request.Start))
            .When(request => EventScheduleRules.IsValid(request.Start) && EventScheduleRules.IsValid(request.End))
            .WithMessage(EventScheduleRules.EndAfterStart);
    }
}
