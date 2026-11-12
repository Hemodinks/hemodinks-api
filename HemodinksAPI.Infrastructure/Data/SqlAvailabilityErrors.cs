namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Erros do SQL Server que significam "o banco nao esta acessivel agora". Nenhum deles carrega valor de linha
/// na mensagem. Fonte unica para a resposta 503 da API e para a sanitizacao de erros de usuario.
/// </summary>
public static class SqlAvailabilityErrors
{
    // Class 20 e como o SqlClient reporta falhas de conexao, cujo numero varia por plataforma.
    public static bool IsUnavailable(int number, byte errorClass) => errorClass == 20 || number is
        -2 // Timeout.
        or 64 or 233 or 10053 or 10054 or 10060 or 10061 or 11001 // Transporte.
        or 40197 or 40501 or 40613 or 49918 or 49919 or 49920 // Azure SQL indisponivel ou ocupado.
        or 10928 or 10929; // Limites de recurso do Azure SQL.

    /// <summary>Indisponibilidade ou deadlock (1205): erros que a estrategia de retry precisa enxergar.</summary>
    public static bool IsTransient(int number, byte errorClass) => number == 1205 || IsUnavailable(number, errorClass);
}
