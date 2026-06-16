using HemodinksAPI.Application.Validation;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public sealed class CreateGrupoMedicoCommandValidator : IRequestValidator<CreateGrupoMedicoCommand>
{
    public void Validate(CreateGrupoMedicoCommand request)
    {
        GrupoMedicoCommandValidator.Validate(request.Nome, request.MedicoUserIds);
    }
}

public sealed class UpdateGrupoMedicoCommandValidator : IRequestValidator<UpdateGrupoMedicoCommand>
{
    public void Validate(UpdateGrupoMedicoCommand request)
    {
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Grupo medico invalido.");
        }

        GrupoMedicoCommandValidator.Validate(request.Nome, request.MedicoUserIds);
    }
}

internal static class GrupoMedicoCommandValidator
{
    public static void Validate(string? nome, List<int>? medicoUserIds)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidOperationException("Nome do grupo medico obrigatorio.");
        }

        if (nome.Trim().Length > 255)
        {
            throw new InvalidOperationException("Nome do grupo medico excede 255 caracteres.");
        }

        if (medicoUserIds == null || medicoUserIds.Count == 0)
        {
            throw new InvalidOperationException("Selecione ao menos um medico para o grupo.");
        }
    }
}
