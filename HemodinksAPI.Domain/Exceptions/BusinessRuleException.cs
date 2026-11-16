namespace HemodinksAPI.Domain.Exceptions;

/// <summary>
/// Regra de negócio violada. A mensagem é escrita para o usuário e é devolvida ao cliente (HTTP 400).
/// Nunca inclua dado interno, sensível ou de outra clínica na mensagem (#293).
/// Herda de <see cref="InvalidOperationException"/> por compatibilidade; uma
/// <see cref="InvalidOperationException"/> genérica é tratada como falha interna (HTTP 500, sem mensagem).
/// </summary>
public class BusinessRuleException : InvalidOperationException
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
