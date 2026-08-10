namespace HemodinksAPI.Application.Features.Common;

/// <summary>
/// Informa se o provider relacional ativo traduz as funcoes Full-Text do SQL Server.
/// Providers usados em testes, como SQLite e InMemory, devem retornar false.
/// </summary>
public interface IFullTextSearchCapability
{
    bool IsSupported { get; }
}
