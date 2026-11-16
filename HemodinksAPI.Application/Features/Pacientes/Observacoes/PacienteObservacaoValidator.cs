using HemodinksAPI.Application.Validation;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class CreatePacienteObservacaoCommandValidator : IRequestValidator<CreatePacienteObservacaoCommand>
{
    public void Validate(CreatePacienteObservacaoCommand request)
    {
        if (request.PacienteId <= 0)
        {
            throw new BusinessRuleException("Paciente invalido para observacao.");
        }

        if (string.IsNullOrWhiteSpace(request.Texto))
        {
            throw new BusinessRuleException("Informe a observacao.");
        }

        if (request.Texto.Trim().Length > 500)
        {
            throw new BusinessRuleException("A observacao deve possuir no maximo 500 caracteres.");
        }

        if (request.ObservacaoPaiId.HasValue && request.ObservacaoPaiId <= 0)
        {
            throw new BusinessRuleException("Observacao pai invalida.");
        }
    }
}
