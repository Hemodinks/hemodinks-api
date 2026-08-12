using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Authentication;

/// <summary>
/// Servico para gerar tokens JWT.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(User user);

    string GenerateToken(User user, Guid? sessionId);

    string GenerateToken(
        UsuarioGlobal usuarioGlobal,
        UsuarioClinica usuarioClinica,
        User user,
        Guid? sessionId);

    string GenerateToken(
        UsuarioGlobal usuarioGlobal,
        UsuarioClinica usuarioClinica,
        User user,
        Equipe? equipe = null,
        EquipeOperador? operador = null,
        bool identificacaoConfiavel = false);
}
