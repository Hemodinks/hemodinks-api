using HemodinksAPI.Api.Validation;

namespace HemodinksAPI.Api.Features.Users.Commands;

public sealed class CreateUserCommandValidator : IRequestValidator<CreateUserCommand>
{
    public void Validate(CreateUserCommand request)
    {
        UserCommandValidator.ValidateProfile(request);
    }
}

public sealed class UpdateUserCommandValidator : IRequestValidator<UpdateUserCommand>
{
    public void Validate(UpdateUserCommand request)
    {
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Usuario invalido.");
        }

        UserCommandValidator.ValidateProfile(request);
    }
}

public sealed class ChangePasswordCommandValidator : IRequestValidator<ChangePasswordCommand>
{
    public void Validate(ChangePasswordCommand request)
    {
        if (request.UserId <= 0)
        {
            throw new InvalidOperationException("Usuario invalido.");
        }

        if (string.IsNullOrWhiteSpace(request.SenhaAtual))
        {
            throw new InvalidOperationException("Informe a senha atual.");
        }

        if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
        {
            throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
        }
    }
}

internal static class UserCommandValidator
{
    public static void ValidateProfile(CreateUserCommand request)
    {
        ValidateProfile(request.Nome, request.Email, request.Cpf);
    }

    public static void ValidateProfile(UpdateUserCommand request)
    {
        ValidateProfile(request.Nome, request.Email, request.Cpf);
    }

    private static void ValidateProfile(string? nome, string? email, string? cpf)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidOperationException("Nome obrigatorio");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email obrigatorio");
        }

        if (string.IsNullOrWhiteSpace(cpf))
        {
            throw new InvalidOperationException("CPF obrigatorio");
        }
    }
}
