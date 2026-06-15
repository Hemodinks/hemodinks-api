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
        ValidateProfile(request.NomePaciente, request.Diagnostico);
    }

    public static void Validate(UpdatePacienteCommand request)
    {
        ValidateProfile(request.NomePaciente, request.Diagnostico);
    }

    private static void ValidateProfile(string? nomePaciente, string? diagnostico)
    {
        if (string.IsNullOrWhiteSpace(nomePaciente))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
        }

        if (diagnostico?.Trim().Length > 1500)
        {
            throw new InvalidOperationException("Diagnostico excede 1500 caracteres");
        }
    }
}
