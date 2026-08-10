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
        ValidateProfile(request.NomePaciente, request.Diagnostico, request.TratamentoMedico, request.OpmeFornecedor);
    }

    public static void Validate(UpdatePacienteCommand request)
    {
        ValidateProfile(request.NomePaciente, request.Diagnostico, request.TratamentoMedico, request.OpmeFornecedor);
    }

    private static void ValidateProfile(string? nomePaciente, string? diagnostico, string? tratamentoMedico, string? opmeFornecedor)
    {
        if (string.IsNullOrWhiteSpace(nomePaciente))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
        }

        if (diagnostico?.Trim().Length > 100) throw new InvalidOperationException("Diagnostico excede 100 caracteres");
        if (tratamentoMedico?.Trim().Length > 100) throw new InvalidOperationException("Tratamento medico excede 100 caracteres");
        if (opmeFornecedor?.Trim().Length > 255) throw new InvalidOperationException("Fornecedor OPME excede 255 caracteres");

    }
}
