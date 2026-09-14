using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace HemodinksAPI.Infrastructure.Data;

public interface ISqlConnectionFactory
{
    DbConnection CreateConnection();
}

public sealed class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public DbConnection CreateConnection() => new SqlConnection(connectionString);
}
