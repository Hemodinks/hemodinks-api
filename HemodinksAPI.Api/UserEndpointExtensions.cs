namespace HemodinksAPI.Api;

/// <summary>
/// Extensoes para mapear endpoints de usuarios.
/// </summary>
public static partial class UserEndpointExtensions
{
    /// <summary>
    /// Mapear endpoints de usuarios.
    /// </summary>
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Criar novo usuario")
            .WithDescription("Cria um novo usuario com a senha padrao")
            .RequireAuthorization("Administrador");

        group.MapPost("/authenticate", AuthenticateUser)
            .WithName("AuthenticateUser")
            .WithSummary("Autenticar usuario")
            .WithDescription("Autentica um usuario e retorna um token JWT")
            .RequireRateLimiting("Login");

        group.MapGet("/", GetAllUsers)
            .WithName("GetAllUsers")
            .WithSummary("Listar todos os usuarios")
            .WithDescription("Retorna uma lista de todos os usuarios cadastrados")
            .RequireAuthorization("UsuariosVisualizar");

        group.MapGet("/perfis", GetAvailableProfiles)
            .WithName("GetAvailableUserProfiles")
            .WithSummary("Listar perfis disponiveis para cadastro de usuarios")
            .WithDescription("Retorna somente os perfis que o usuario autenticado pode atribuir")
            .RequireAuthorization("Administrador");

        group.MapGet("/{id}", GetUserById)
            .WithName("GetUserById")
            .WithSummary("Buscar usuario por ID")
            .WithDescription("Retorna os dados de um usuario especifico")
            .RequireAuthorization();

        group.MapGet("/{id}/foto-perfil", GetProfilePhoto)
            .WithName("GetUserProfilePhoto")
            .WithSummary("Buscar foto de perfil")
            .WithDescription("Retorna a foto de perfil pelo storage configurado no ambiente")
            .RequireAuthorization();

        group.MapGet("/email/{email}", GetUserByEmail)
            .WithName("GetUserByEmail")
            .WithSummary("Buscar usuario por email")
            .WithDescription("Retorna os dados de um usuario pelo email")
            .RequireAuthorization("Administrador");

        group.MapPut("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithSummary("Atualizar usuario")
            .WithDescription("Atualiza os dados cadastrais de um usuario")
            .RequireAuthorization();

        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Excluir usuario")
            .WithDescription("Remove um usuario cadastrado")
            .RequireAuthorization("Administrador");

        group.MapPut("/{id}/password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Alterar senha")
            .WithDescription("Altera a senha do usuario autenticado")
            .RequireAuthorization();

        group.MapPost("/password/reset", ResetPasswordByEmail)
            .WithName("ResetPasswordByEmail")
            .WithSummary("Resetar senha por email")
            .WithDescription("Solicita um token temporario para redefinicao de senha. Com PasswordReset__UseEmail=true, a API prioriza Function HTTP valida, depois fila Azure e por fim SMTP. Envie Idempotency-Key para tornar retries seguros.")
            .RequireRateLimiting("PasswordReset");

        group.MapPost("/password/reset/confirm", ConfirmPasswordReset)
            .WithName("ConfirmPasswordReset")
            .WithSummary("Confirmar reset de senha")
            .WithDescription("Redefine a senha usando o token temporario gerado anteriormente. Envie Idempotency-Key para tornar retries seguros.")
            .RequireRateLimiting("PasswordReset");

        group.MapPut("/{id}/password/reset", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Resetar senha")
            .WithDescription("Reseta a senha do usuario para a senha padrao e obriga troca no proximo login")
            .RequireAuthorization("Administrador");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadUserArquivo")
            .WithSummary("Enviar arquivo do cadastro medico")
            .WithDescription("Adiciona documento ao cadastro de um usuario medico")
            .DisableAntiforgery()
            .RequireAuthorization();

        group.MapGet("/{id}/arquivos/{arquivoId}/download", DownloadArquivo)
            .WithName("DownloadUserArquivo")
            .WithSummary("Baixar arquivo do cadastro medico")
            .RequireAuthorization();

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeleteUserArquivo")
            .WithSummary("Excluir arquivo do cadastro medico")
            .RequireAuthorization();
    }
}
