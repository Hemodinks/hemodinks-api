using System.Text.Json.Serialization;
using FluentValidation;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Validation;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class ChangeTemporaryPasswordCommand : IRequest<ChangePasswordResponse>
{
    [JsonIgnore] public CurrentUserContext? CurrentUser { get; set; }
    [JsonIgnore] public Guid SecurityVersion { get; set; }
    public string NovaSenha { get; set; } = "";
    public string Confirmacao { get; set; } = "";
}

public sealed class ChangeTemporaryPasswordCommandValidator : AbstractValidator<ChangeTemporaryPasswordCommand>, IRequestValidator<ChangeTemporaryPasswordCommand>
{
    public ChangeTemporaryPasswordCommandValidator()
    {
        RuleFor(x => x.NovaSenha).NotEmpty().MinimumLength(8).MaximumLength(128)
            .WithMessage("A nova senha deve ter entre 8 e 128 caracteres.");
        RuleFor(x => x.Confirmacao).Equal(x => x.NovaSenha).WithMessage("A confirmação precisa ser igual à nova senha.");
    }
    void IRequestValidator<ChangeTemporaryPasswordCommand>.Validate(ChangeTemporaryPasswordCommand request)
    {
        var result = Validate(request);
        if (!result.IsValid) throw new InvalidOperationException(result.Errors[0].ErrorMessage);
        PasswordCommandRules.ValidatePasswordChangeCandidate(request.NovaSenha);
    }
}

public sealed class ChangeTemporaryPasswordCommandHandler(TemporaryAccessService service)
    : IRequestHandler<ChangeTemporaryPasswordCommand, ChangePasswordResponse>
{
    public Task<ChangePasswordResponse> Handle(ChangeTemporaryPasswordCommand request, CancellationToken cancellationToken)
        => service.CompleteAsync(request, cancellationToken);
}
