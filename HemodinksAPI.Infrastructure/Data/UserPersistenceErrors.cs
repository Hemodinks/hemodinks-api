using System.Text.RegularExpressions;
using HemodinksAPI.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// O SQL Server repete na mensagem de erro o valor que violou um indice
/// ("The duplicate key value is (1, 52998224725)") ou foi truncado. Numa gravacao de
/// <see cref="User"/> isso e CPF ou email, e a excecao chega ao log pelo EF, pelos
/// handlers e pelo middleware de erros. Aqui a excecao e trocada por outra so com o
/// numero do erro e o nome do indice (#227).
/// </summary>
public static partial class UserPersistenceErrors
{
    // Entries pode vir vazio quando o EF nao identifica a linha do lote que falhou; por isso
    // tambem conta qualquer usuario pendente no ChangeTracker.
    // Erros transitorios (banco indisponivel, timeout, deadlock) nao trazem valor de linha e precisam manter o
    // SqlException na cadeia: e por ele que a API responde 503 e que a estrategia de retry repete.
    internal static bool ShouldSanitize(DbUpdateException exception, ChangeTracker changeTracker) =>
        exception is not DbUpdateConcurrencyException
        && !IsTransient(exception)
        && (exception.Entries.Any(entry => entry.Entity is User)
            || changeTracker.Entries<User>().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));

    internal static DbUpdateException Sanitize(DbUpdateException exception)
    {
        var sqlNumber = FindSqlErrorNumber(exception);
        var constraint = FindConstraintName(exception);
        var message = $"Falha ao gravar usuario (erro SQL {sqlNumber?.ToString() ?? "-"}, restricao {constraint ?? "-"}).";
        // Sem InnerException: e nela que esta o valor. Os catch existentes filtram por
        // DbUpdateException, entao o tipo continua o mesmo.
        return new DbUpdateException(message, (Exception?)null, exception.Entries);
    }

    /// <summary>
    /// Os eventos do EF que registram a excecao bruta passam para Debug. Quem propaga a
    /// excecao continua registrada pelo middleware de erros, ja sanitizada no caso de usuario.
    /// </summary>
    public static DbContextOptionsBuilder SuppressRawDatabaseErrorLogs(this DbContextOptionsBuilder options) =>
        options.ConfigureWarnings(warnings => warnings.Log(
            (CoreEventId.SaveChangesFailed, LogLevel.Debug),
            (RelationalEventId.CommandError, LogLevel.Debug)));

    private static bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && SqlAvailabilityErrors.IsTransient(sql.Number, sql.Class))
            {
                return true;
            }
        }

        return false;
    }

    private static int? FindSqlErrorNumber(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql.Number;
            }
        }

        return null;
    }

    private static string? FindConstraintName(Exception exception)
    {
        for (Exception? current = exception.InnerException; current != null; current = current.InnerException)
        {
            var match = ConstraintName().Match(current.Message);
            if (match.Success)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    // Nomes gerados pelo EF (IX_, PK_, FK_, AK_, CK_). So o identificador, nunca o resto da mensagem.
    [GeneratedRegex(@"'(?<name>(?:IX|PK|FK|AK|CK)_[A-Za-z0-9_]{1,120})'", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ConstraintName();
}
