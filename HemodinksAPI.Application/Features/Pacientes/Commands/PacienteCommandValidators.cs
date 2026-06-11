using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public sealed class CreatePacienteCommandValidator : IRequestValidator<CreatePacienteCommand>
{
    public void Validate(CreatePacienteCommand request)
    {
        PacienteCommandValidator.Validate(request);
    }
}

public sealed class UpdatePacienteCommandValidator : IRequestValidator<UpdatePacienteCommand>
{
    public void Validate(UpdatePacienteCommand request)
    {
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Paciente invalido.");
        }

        PacienteCommandValidator.Validate(request);
    }
}

internal static class PacienteCommandValidator
{
    public static void Validate(CreatePacienteCommand request)
    {
        ValidateProfile(request.NomePaciente, request.Email, request.Cpf);
    }

    public static void Validate(UpdatePacienteCommand request)
    {
        ValidateProfile(request.NomePaciente, request.Email, request.Cpf);
    }

    private static void ValidateProfile(string? nomePaciente, string? email, string? cpf)
    {
        if (string.IsNullOrWhiteSpace(nomePaciente))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
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
