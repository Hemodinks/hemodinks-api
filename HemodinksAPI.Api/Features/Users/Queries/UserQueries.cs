using HemodinksAPI.Api.Features.Common;
using HemodinksAPI.Api.Models;
using MediatR;

namespace HemodinksAPI.Api.Features.Users.Queries;

public class UserDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? Cpf { get; set; }
    public string? FotoPerfil { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public int PerfilId { get; set; }
    public string PerfilNome { get; set; } = null!;
}

public class GetAllUsersQuery : IRequest<PagedResult<UserDto>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public int? ProfileId { get; set; }
}

public class GetUserByIdQuery : IRequest<UserDto?>
{
    public int Id { get; set; }

    public GetUserByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetUserByEmailQuery : IRequest<UserDto?>
{
    public string Email { get; set; } = null!;

    public GetUserByEmailQuery(string email)
    {
        Email = email;
    }
}
