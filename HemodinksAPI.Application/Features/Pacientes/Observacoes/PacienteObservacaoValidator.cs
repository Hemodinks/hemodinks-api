using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class CreatePacienteObservacaoCommandValidator : IRequestValidator<CreatePacienteObservacaoCommand>
{
    public void Validate(CreatePacienteObservacaoCommand request)
    {
        if (request.PacienteId <= 0)
        {
            throw new InvalidOperationException("Paciente invalido para observacao.");
        }

        if (string.IsNullOrWhiteSpace(request.Texto))
        {
            throw new InvalidOperationException("Informe a observacao.");
        }

        if (request.Texto.Trim().Length > 500)
        {
            throw new InvalidOperationException("A observacao deve possuir no maximo 500 caracteres.");
        }

        if (request.ObservacaoPaiId.HasValue && request.ObservacaoPaiId <= 0)
        {
            throw new InvalidOperationException("Observacao pai invalida.");
        }
    }
}
