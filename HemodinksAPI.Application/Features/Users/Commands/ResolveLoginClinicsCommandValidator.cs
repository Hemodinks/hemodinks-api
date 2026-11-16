using HemodinksAPI.Application.Validation;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class ResolveLoginClinicsCommandValidator : IRequestValidator<ResolveLoginClinicsCommand>
{
    public void Validate(ResolveLoginClinicsCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) throw new BusinessRuleException("Email obrigatorio");
        if (string.IsNullOrWhiteSpace(request.Senha)) throw new BusinessRuleException("Senha obrigatoria");
    }
}
