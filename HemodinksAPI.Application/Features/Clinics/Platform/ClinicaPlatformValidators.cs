using FluentValidation;
using HemodinksAPI.Domain.Utils;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed class CreateClinicaRequestValidator : AbstractValidator<CreateClinicaRequest>
{
    public CreateClinicaRequestValidator()
    {
        RuleFor(request => request.Cnpj)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe um CNPJ valido.")
            .Must(CnpjUtils.IsValid)
            .WithMessage("Informe um CNPJ valido.");
    }
}

public sealed class UpdateClinicaRequestValidator : AbstractValidator<UpdateClinicaRequest>
{
    public UpdateClinicaRequestValidator()
    {
        When(request => request.Cnpj is not null, () =>
        {
            RuleFor(request => request.Cnpj)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("Informe um CNPJ valido.")
                .Must(CnpjUtils.IsValid)
                .WithMessage("Informe um CNPJ valido.");
        });
    }
}
