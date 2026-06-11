using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Authorization;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class UserDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? Cpf { get; set; }
    public string? Crm { get; set; }
    public string? CrmUf { get; set; }
    public string? FotoPerfil { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public int PerfilId { get; set; }
    public string PerfilNome { get; set; } = null!;
    public int ArquivosCount { get; set; }
    public List<UserArquivoDto> Arquivos { get; set; } = [];
}

public class UserArquivoDto
{
    public int Id { get; set; }
    public string NomeOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string Url { get; set; } = null!;
    public DateTime DataUpload { get; set; }
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

    public CurrentUserContext? CurrentUser { get; set; }

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

public class GetUserProfilePhotoQuery : IRequest<UserProfilePhotoDto?>
{
    public int Id { get; set; }

    public CurrentUserContext CurrentUser { get; set; } = null!;
}

public sealed class UserProfilePhotoDto : IDisposable
{
    public Stream Content { get; set; } = Stream.Null;

    public string ContentType { get; set; } = "application/octet-stream";

    public void Dispose()
    {
        Content.Dispose();
    }
}
