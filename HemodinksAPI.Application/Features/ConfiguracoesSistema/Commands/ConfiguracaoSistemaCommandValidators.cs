using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.ConfiguracoesSistema.Commands;

public sealed class UpdateConfiguracaoSistemaCommandValidator : IRequestValidator<UpdateConfiguracaoSistemaCommand>
{
    private const int MaxPhotoLength = 2_000_000;

    public void Validate(UpdateConfiguracaoSistemaCommand request)
    {
        var nomeEmpresa = request.NomeEmpresa?.Trim();
        var fotoEmpresa = request.FotoEmpresa?.Trim();

        if (string.IsNullOrWhiteSpace(nomeEmpresa))
        {
            throw new InvalidOperationException("Informe o nome da empresa.");
        }

        if (nomeEmpresa.Length > 120)
        {
            throw new InvalidOperationException("Nome da empresa excede 120 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(fotoEmpresa))
        {
            return;
        }

        if (fotoEmpresa.Length > MaxPhotoLength)
        {
            throw new InvalidOperationException("Foto da empresa excede o limite permitido.");
        }

        if (fotoEmpresa.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !fotoEmpresa.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use uma imagem valida para a foto da empresa.");
        }
    }
}
