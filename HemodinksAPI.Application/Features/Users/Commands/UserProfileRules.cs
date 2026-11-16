using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Domain.Exceptions;

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

    public static void EnsureAssignablePerfilId(int perfilId, CurrentUserContext? currentUser = null)
    {
        if (perfilId == Perfil.SuperAdministradorId && currentUser?.IsSuperAdministrador != true)
        {
            throw new UnauthorizedAccessException("Perfil SuperAdministrador somente pode ser atribuido por outro SuperAdministrador");
        }

        if (perfilId == Perfil.PacientesId)
        {
            throw new BusinessRuleException("Perfil Pacientes desativado para cadastro de usuarios");
        }

        if (perfilId == Perfil.EquipeId)
        {
            throw new BusinessRuleException("Perfil Equipe deve ser criado pelo gerenciamento de equipes");
        }
    }

    public static string GetPerfilNome(User user)
    {
        return user.Perfil?.Nome ?? string.Empty;
    }

    public static string? NormalizeCpf(string? cpf)
    {
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
            throw new BusinessRuleException("CRM obrigatorio para medicos");
        }

        if (normalizedCrm.Length > MaxCrmLength)
        {
            throw new BusinessRuleException($"CRM deve ter no maximo {MaxCrmLength} caracteres");
        }

        if (string.IsNullOrWhiteSpace(normalizedCrmUf))
        {
            throw new BusinessRuleException("UF do CRM obrigatoria para medicos");
        }

        if (!ValidBrazilUf.Contains(normalizedCrmUf))
        {
            throw new BusinessRuleException("UF do CRM invalida");
        }

        return (normalizedCrm, normalizedCrmUf);
    }
}
