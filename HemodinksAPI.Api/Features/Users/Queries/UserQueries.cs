using HemodinksAPI.Api.Models;
using MediatR;

namespace HemodinksAPI.Api.Features.Users.Queries;

/// <summary>
/// DTO para resposta de usuário
/// </summary>
public class UserDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public DateTime DataCadastro { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
}

/// <summary>
/// Query para buscar todos os usuários
/// </summary>
public class GetAllUsersQuery : IRequest<List<UserDto>>
{
}

/// <summary>
/// Query para buscar usuário por ID
/// </summary>
public class GetUserByIdQuery : IRequest<UserDto?>
{
    public int Id { get; set; }

    public GetUserByIdQuery(int id)
    {
        Id = id;
    }
}

/// <summary>
/// Query para buscar usuário por email
/// </summary>
public class GetUserByEmailQuery : IRequest<UserDto?>
{
    public string Email { get; set; } = null!;

    public GetUserByEmailQuery(string email)
    {
        Email = email;
    }
}
