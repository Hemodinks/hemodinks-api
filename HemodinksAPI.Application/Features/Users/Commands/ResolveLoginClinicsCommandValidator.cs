using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class ResolveLoginClinicsCommandValidator : IRequestValidator<ResolveLoginClinicsCommand>
{
    public void Validate(ResolveLoginClinicsCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) throw new InvalidOperationException("Email obrigatorio");
        if (string.IsNullOrWhiteSpace(request.Senha)) throw new InvalidOperationException("Senha obrigatoria");
    }
}
