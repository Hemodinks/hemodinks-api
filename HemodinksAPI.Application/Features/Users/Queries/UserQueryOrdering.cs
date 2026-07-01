using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Users.Queries;

internal static class UserQueryOrdering
{
    public static IQueryable<User> ApplyOrdering(IQueryable<User> query, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSortBy switch
        {
            "nome" => isDescending
                ? query.OrderByDescending(user => user.Nome).ThenByDescending(user => user.Id)
                : query.OrderBy(user => user.Nome).ThenBy(user => user.Id),
            "perfil" => isDescending
                ? query.OrderByDescending(user => user.Perfil.Nome).ThenByDescending(user => user.Id)
                : query.OrderBy(user => user.Perfil.Nome).ThenBy(user => user.Id),
            "email" => isDescending
                ? query.OrderByDescending(user => user.Email).ThenByDescending(user => user.Id)
                : query.OrderBy(user => user.Email).ThenBy(user => user.Id),
            "ativo" => isDescending
                ? query.OrderByDescending(user => user.Ativo).ThenByDescending(user => user.Nome).ThenByDescending(user => user.Id)
                : query.OrderBy(user => user.Ativo).ThenBy(user => user.Nome).ThenBy(user => user.Id),
            _ => isDescending
                ? query.OrderByDescending(user => user.DataAtualizacao ?? user.DataCadastro).ThenBy(user => user.Nome).ThenBy(user => user.Id)
                : query.OrderBy(user => user.DataAtualizacao ?? user.DataCadastro).ThenBy(user => user.Nome).ThenBy(user => user.Id),
        };
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "recent"
            : sortBy.Trim().ToLowerInvariant();
    }
}
