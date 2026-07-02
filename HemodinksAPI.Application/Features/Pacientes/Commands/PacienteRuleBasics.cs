namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static void ValidateNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
        }
    }

    public static string ResolveTelefone(string? telefone)
    {
        return TrimOptional(telefone) ?? string.Empty;
    }

    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? TrimAndValidateOptional(string? value, int maxLength, string errorMessage)
    {
        var trimmed = TrimOptional(value);
        if (trimmed?.Length > maxLength)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return trimmed;
    }
}
