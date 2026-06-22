using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;

public sealed class UpdateConfiguracaoSistemaCommandValidator : IRequestValidator<UpdateConfiguracaoSistemaCommand>
{
    public void Validate(UpdateConfiguracaoSistemaCommand request)
    {
        var nomeEmpresa = request.NomeEmpresa?.Trim();

        if (string.IsNullOrWhiteSpace(nomeEmpresa))
        {
            throw new InvalidOperationException("Informe o nome da empresa.");
        }

        if (nomeEmpresa.Length > 120)
        {
            throw new InvalidOperationException("Nome da empresa excede 120 caracteres.");
        }
    }
}
