using FluentValidation;

namespace HemodinksAPI.Application.Features.Events;

internal sealed class AgendaRecipientQueryValidator : AbstractValidator<GetAgendaNotificationRecipientOptionsQuery>
{
    public AgendaRecipientQueryValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100).WithMessage("A busca deve ter no máximo 100 caracteres.");
        RuleFor(query => query.Profile).Must(profile => profile is null or "" or "all" or "medical" or "administrative")
            .WithMessage("Informe um filtro de perfil válido.");
        RuleFor(query => query.Page).InclusiveBetween(1, 100000).WithMessage("Informe uma página válida.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 50).WithMessage("A página deve conter entre 1 e 50 usuários.");
    }
}
