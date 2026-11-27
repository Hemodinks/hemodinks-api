using System.Collections.Concurrent;
using System.Security.Cryptography;
using HemodinksAPI.Application.Utils;

namespace HemodinksAPI.Application.Authentication;

/// <summary>
/// #431: iguala o custo de uma falha de login sem verificacao de senha (conta inexistente, bloqueada ou
/// inativa) ao de uma senha errada, executando uma verificacao contra um hash de referencia gerado pelo
/// proprio hasher (mesmo formato e iteracoes). O valor de origem e aleatorio e descartado; o resultado
/// da verificacao e sempre ignorado.
/// </summary>
public static class PasswordTimingEqualizer
{
    // Por tipo, nao por instancia: o hasher e scoped e gerar o hash a cada request dobraria o custo da falha.
    private static readonly ConcurrentDictionary<Type, string> ReferenceHashes = new();

    public static void VerifyAgainstReference(IPasswordHasher hasher, string? password)
    {
        var reference = ReferenceHashes.GetOrAdd(hasher.GetType(), _ =>
            hasher.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        // Senha repassada sem alteracao: o hasher trata nulo/vazio igual ao caminho de uma conta real.
        _ = hasher.VerifyPassword(password!, reference);
    }
}
