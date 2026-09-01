using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal static partial class PacienteRules
{
    public static async Task<string?> NormalizeAndValidateCpfAsync(
        IPatientFeatureDbContext context,
        string? cpf,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return null;
        }

        if (!CpfUtils.IsValid(cpf))
        {
            throw new InvalidOperationException("CPF invalido");
        }

        var normalizedCpf = CpfUtils.Normalize(cpf)!;
        var cpfAlreadyExists = await context.Users
            .AnyAsync(u => u.Cpf == normalizedCpf && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (cpfAlreadyExists)
        {
            throw new InvalidOperationException("CPF ja cadastrado");
        }

        return normalizedCpf;
    }

    public static async Task<string> ResolveEmailAsync(
        IPatientFeatureDbContext context,
        string? email,
        string? cpf,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        var resolvedEmail = string.IsNullOrWhiteSpace(email)
            ? GenerateTechnicalEmail(cpf)
            : email.Trim();

        var emailAlreadyExists = await context.Users
            .AnyAsync(u => u.Email == resolvedEmail && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (emailAlreadyExists)
        {
            throw new InvalidOperationException("Email ja cadastrado");
        }

        return resolvedEmail;
    }

    public static async Task ValidateEmailAsync(
        IPatientFeatureDbContext context,
        string email,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email obrigatorio");
        }

        var trimmedEmail = email.Trim();
        var emailAlreadyExists = await context.Users
            .AnyAsync(u => u.Email == trimmedEmail && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (emailAlreadyExists)
        {
            throw new InvalidOperationException("Email ja cadastrado");
        }
    }

    private static string GenerateTechnicalEmail(string? cpf)
    {
        return !string.IsNullOrWhiteSpace(cpf)
            ? $"paciente-{cpf}@hemodinks.local"
            : $"paciente-{Guid.NewGuid():N}@hemodinks.local";
    }
}
