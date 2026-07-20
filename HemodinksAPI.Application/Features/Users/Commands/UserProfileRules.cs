using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class UserProfileRules
{
    private const int MaxCrmLength = 20;

    private static readonly HashSet<string> ValidBrazilUf = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
        "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    public static int NormalizePerfilId(int perfilId)
    {
        return perfilId == 0 ? Perfil.MedicosId : perfilId;
    }

    public static void EnsureAssignablePerfilId(int perfilId)
    {
        if (perfilId == Perfil.SuperAdministradorId)
        {
            throw new UnauthorizedAccessException("Perfil SuperAdministrador somente pode ser atribuido pela plataforma");
        }

        if (perfilId == Perfil.PacientesId)
        {
            throw new InvalidOperationException("Perfil Pacientes desativado para cadastro de usuarios");
        }
    }

    public static string GetPerfilNome(User user)
    {
        return user.Perfil?.Nome ?? string.Empty;
    }

    public static string? NormalizeAndValidateCpf(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return null;
        }

        if (!CpfUtils.IsValid(cpf))
        {
            throw new InvalidOperationException("CPF invalido");
        }

        return CpfUtils.Normalize(cpf);
    }

    public static (string? Crm, string? CrmUf) NormalizeAndValidateMedicalRegistration(
        string? crm,
        string? crmUf,
        int perfilId)
    {
        if (perfilId != Perfil.MedicosId)
        {
            return (null, null);
        }

        var normalizedCrm = crm?.Trim();
        var normalizedCrmUf = crmUf?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCrm))
        {
            throw new InvalidOperationException("CRM obrigatorio para medicos");
        }

        if (normalizedCrm.Length > MaxCrmLength)
        {
            throw new InvalidOperationException($"CRM deve ter no maximo {MaxCrmLength} caracteres");
        }

        if (string.IsNullOrWhiteSpace(normalizedCrmUf))
        {
            throw new InvalidOperationException("UF do CRM obrigatoria para medicos");
        }

        if (!ValidBrazilUf.Contains(normalizedCrmUf))
        {
            throw new InvalidOperationException("UF do CRM invalida");
        }

        return (normalizedCrm, normalizedCrmUf);
    }
}
