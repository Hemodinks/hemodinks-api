namespace HemodinksAPI.Application.Security;

public sealed class SensitiveIdentityException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public static SensitiveIdentityException Invalid() => new("identity_revalidation_failed", "Não foi possível confirmar sua identidade para esta ação. Revise os dados ou entre novamente.");
    public static SensitiveIdentityException EmailConfirmationAttemptsExceeded() => new("email_confirmation_attempts_exceeded", "Muitas tentativas com código incorreto. Solicite um novo código para alterar seu email.");
    public static SensitiveIdentityException EmailConfirmationRequired() => new("email_confirmation_required", "Use a alteração de email com confirmação do novo endereço.");
}
