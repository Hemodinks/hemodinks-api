using FluentValidation;

namespace HemodinksAPI.Application.Features.Events.Queries;

internal sealed class EventQueryValidator : AbstractValidator<GetEventsQuery>
{
    public EventQueryValidator()
    {
        RuleFor(query => query.FromDate).Must((query, _) =>
            (!query.FromDate.HasValue && !query.ToDate.HasValue) ||
            (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate <= query.ToDate && query.From.HasValue && query.To.HasValue))
            .WithMessage("Informe um intervalo de datas válido junto ao período da agenda.");
        RuleFor(query => query.Search).MaximumLength(200)
            .WithMessage("A busca deve ter no máximo 200 caracteres.");
        RuleFor(query => query.UserId).GreaterThan(0).When(query => query.UserId.HasValue)
            .WithMessage("Informe um responsável válido.");
        RuleFor(query => query.To).Must((query, to) => !query.From.HasValue || !to.HasValue ||
            EventFeatureRules.ToUtc(to.Value) >= EventFeatureRules.ToUtc(query.From.Value))
            .WithMessage("O fim do período deve ser igual ou posterior ao início.");
    }
}
