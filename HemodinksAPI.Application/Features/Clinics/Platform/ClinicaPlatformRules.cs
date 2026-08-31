using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using System.Text.RegularExpressions;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

internal static class ClinicaPlatformRules
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    internal static IQueryable<User> ClinicEmployees(IPlatformTeamDbContext context)
    {
        return context.Users
            .Where(user => (user.PerfilId == Perfil.AdministradorId
                    || user.PerfilId == Perfil.MedicosId
                    || user.PerfilId == Perfil.ControllerId
                    || user.PerfilId == Perfil.EquipeId)
                && !context.Equipes.Any(team => team.UsuarioLoginId == user.Id));
    }

    internal static string NormalizeSlug(string? value)
    {
        var slug = RequireText(value, "Slug da clinica obrigatorio", 120).ToLowerInvariant();
        if (!SlugPattern.IsMatch(slug))
        {
            throw new InvalidOperationException("Slug deve conter apenas letras minusculas, numeros e hifens");
        }

        return slug;
    }

    internal static string NormalizePlano(string? value)
    {
        var plano = string.IsNullOrWhiteSpace(value) ? ClinicaPlanos.Trial : value.Trim();
        if (plano.Equals(ClinicaPlanos.Trial, StringComparison.OrdinalIgnoreCase)) return ClinicaPlanos.Trial;
        if (plano.Equals(ClinicaPlanos.Completa, StringComparison.OrdinalIgnoreCase)) return ClinicaPlanos.Completa;
        if (plano.Equals(ClinicaPlanos.Parcial, StringComparison.OrdinalIgnoreCase)) return ClinicaPlanos.Parcial;
        throw new InvalidOperationException("Plano deve ser Trial, Parcial ou Completa");
    }

    internal static string? NormalizeModulos(string plano, IEnumerable<string>? values)
    {
        if (plano != ClinicaPlanos.Parcial) return null;

        var requested = values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToList() ?? [];
        var invalid = requested.FirstOrDefault(value =>
            !ClinicaModulos.Todos.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase));
        if (invalid != null) throw new InvalidOperationException($"Modulo invalido: {invalid}");

        var normalized = ClinicaModulos.Todos
            .Where(allowed => requested.Contains(allowed, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (normalized.Count == 0)
        {
            throw new InvalidOperationException("Selecione ao menos um modulo para o plano Parcial");
        }

        return string.Join(',', normalized);
    }

    internal static string RequireText(string? value, string message, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    internal static string NormalizeOptional(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new InvalidOperationException($"Valor deve ter no maximo {maxLength} caracteres");
    }
}
