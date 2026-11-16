namespace HemodinksAPI.Domain.Exceptions;

/// <summary>
/// Recurso não encontrado (ou não visível para a clínica atual). A mensagem é escrita para o usuário e é
/// devolvida ao cliente (HTTP 404); não revela se o registro existe em outra clínica (#293).
/// Uma <see cref="KeyNotFoundException"/> genérica (ex.: de Dictionary) devolve mensagem fixa.
/// </summary>
public class ResourceNotFoundException : KeyNotFoundException
{
    public const string DefaultMessage = "Registro nao encontrado.";

    public ResourceNotFoundException()
        : base(DefaultMessage)
    {
    }

    public ResourceNotFoundException(string message)
        : base(message)
    {
    }
}
