using System.Data.Common;
using System.Xml.Linq;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Persiste o anel de chaves do Data Protection do CPF na tabela DataProtectionKeys, compartilhada
/// por todas as replicas (blue/green). O XML chega aqui ja cifrado pelo certificado.
/// ADO direto para nao depender do AppDbContext (que depende do protetor) nem de pacote novo.
/// </summary>
internal sealed class SqlDataProtectionXmlRepository(ISqlConnectionFactory connectionFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Xml] FROM [dbo].[DataProtectionKeys] ORDER BY [Id]";

        var elements = new List<XElement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [dbo].[DataProtectionKeys] ([FriendlyName], [Xml]) VALUES (@friendlyName, @xml)";
        AddParameter(command, "@friendlyName", friendlyName);
        AddParameter(command, "@xml", element.ToString(SaveOptions.DisableFormatting));
        command.ExecuteNonQuery();
    }

    private static void AddParameter(DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
