namespace HemodinksAPI.Api.Features.Users.Commands;

/// <summary>
/// DTO para criação de usuário
/// </summary>
public partial class CreateUserCommand
{
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public DateTime DataNascimento { get; set; }
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
    public DateTime DataCadastro { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public bool PrecisaTrocarSenha { get; set; }
}

/// <summary>
/// DTO para autenticação de usuário
/// </summary>
public partial class AuthenticateUserCommand
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
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Token { get; set; } = null!;
    public bool PrecisaTrocarSenha { get; set; }
}

/// <summary>
/// DTO para atualização de usuário
/// </summary>
public partial class UpdateUserCommand
{
    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
}

/// <summary>
/// DTO para exclusão de usuário
/// </summary>
public partial class DeleteUserCommand
{
    public int Id { get; set; }
}

/// <summary>
/// DTO para troca de senha do usuário autenticado
/// </summary>
public partial class ChangePasswordCommand
{
    public int UserId { get; set; }
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
