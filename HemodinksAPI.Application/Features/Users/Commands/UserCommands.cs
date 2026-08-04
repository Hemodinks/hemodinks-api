using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Queries;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// DTO para criação de usuário
/// </summary>
public partial class CreateUserCommand : IRequest<CreateUserResponse>
{
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? Cpf { get; set; }
    public string? Crm { get; set; }
    public string? CrmUf { get; set; }
    public string? FotoPerfil { get; set; }
    public DateTime? DataNascimento { get; set; }
    public int PerfilId { get; set; } = Perfil.MedicosId;
}

/// <summary>
/// DTO para resposta de criação de usuário
/// </summary>
public class CreateUserResponse
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
    public DateTime? DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public int PerfilId { get; set; }
    public string PerfilNome { get; set; } = null!;
}

/// <summary>
/// DTO para autenticação de usuário
/// </summary>
public partial class AuthenticateUserCommand : IRequest<AuthenticateUserResponse>
{
    public string Email { get; set; } = null!;
    public string Senha { get; set; } = null!;
}

/// <summary>
/// DTO para resposta de autenticação
/// </summary>
public class AuthenticateUserResponse
{
    public int Id { get; set; }
    public int UsuarioGlobalId { get; set; }
    public int ClinicaId { get; set; }
    public string ClinicaSlug { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Token { get; set; }
    public string? Cpf { get; set; }
    public string? Crm { get; set; }
    public string? CrmUf { get; set; }
    public string? FotoPerfil { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public bool PrecisaTrocarPin { get; set; }
    public int PerfilId { get; set; }
    public string PerfilNome { get; set; } = null!;
    public IReadOnlyList<string> ModulosLiberados { get; set; } = [];
    public LicencaDto? Licenca { get; set; }
    public EquipeLoginChallengeDto? EquipeDesafio { get; set; }
}

public sealed record EquipeLoginChallengeDto(
    string Token,
    int EquipeId,
    string EquipeNome,
    string ModoIdentificacao,
    DateTime ExpiraEm,
    IReadOnlyList<EquipeOperadorLoginDto> Operadores);

public sealed record EquipeOperadorLoginDto(int Id, string Nome, bool ExigePin);

/// <summary>
/// DTO para atualização de usuário
/// </summary>
public partial class UpdateUserCommand : IRequest<UserDto>
{
    public int Id { get; set; }
    public CurrentUserContext? CurrentUser { get; set; }
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? Cpf { get; set; }
    public string? Crm { get; set; }
    public string? CrmUf { get; set; }
    public string? FotoPerfil { get; set; }
    public DateTime? DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public int PerfilId { get; set; } = Perfil.MedicosId;
}

/// <summary>
/// DTO para exclusão de usuário
/// </summary>
public partial class DeleteUserCommand : IRequest
{
    public int Id { get; set; }
}

public partial class UploadUserArquivoCommand : IRequest<UserArquivoDto>
{
    public int UserId { get; set; }
    public IFormFile File { get; set; } = null!;
    public CurrentUserContext? CurrentUser { get; set; }
}

public partial class DeleteUserArquivoCommand : IRequest
{
    public int UserId { get; set; }
    public int ArquivoId { get; set; }
    public CurrentUserContext? CurrentUser { get; set; }
}

/// <summary>
/// DTO para troca de senha do usuário autenticado
/// </summary>
public partial class ChangePasswordCommand : IRequest<ChangePasswordResponse>
{
    public int UserId { get; set; }
    public CurrentUserContext? CurrentUser { get; set; }
    public string SenhaAtual { get; set; } = null!;
    public string NovaSenha { get; set; } = null!;
}

/// <summary>
/// DTO para resposta de troca de senha
/// </summary>
public class ChangePasswordResponse
{
    public int Id { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>
/// DTO para reset de senha do usuario.
/// </summary>
public partial class ResetUserPasswordCommand : IRequest<ResetUserPasswordResponse>
{
    public int UserId { get; set; }
}

/// <summary>
/// DTO para solicitar reset de senha pelo email do usuario.
/// </summary>
public partial class ResetUserPasswordByEmailCommand : IRequest<RequestPasswordResetResponse>
{
    public string Email { get; set; } = null!;

    public string? RequestIp { get; set; }
}

public partial class ConfirmPasswordResetCommand : IRequest<ResetUserPasswordResponse>
{
    public string Token { get; set; } = null!;

    public string NovaSenha { get; set; } = null!;
}

public class RequestPasswordResetResponse
{
    public int? Id { get; set; }

    public bool? PrecisaTrocarSenha { get; set; }

    public string Message { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public string? DebugToken { get; set; }

    public string? Mode { get; set; }
}

public class PasswordResetOptions
{
    public bool UseEmail { get; set; } = true;

    public bool ComEmail
    {
        get => UseEmail;
        set => UseEmail = value;
    }

    public bool ExposeTokenInResponse { get; set; }
}

/// <summary>
/// DTO para resposta de reset de senha.
/// </summary>
public class ResetUserPasswordResponse
{
    public int Id { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
    public string Message { get; set; } = null!;
}
