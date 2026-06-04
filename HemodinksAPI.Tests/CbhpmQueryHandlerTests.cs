using HemodinksAPI.Api.Features.Cbhpm.Commands;
using HemodinksAPI.Api.Features.Cbhpm.Queries;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class CbhpmQueryHandlerTests
{
    [Fact]
    public async Task ImportCbhpmGeral_InsertsAndUpdatesByCodigo()
    {
        await using var context = TestDbContextFactory.Create();
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Descricao antiga",
            Porte = "1A"
        });
        await context.SaveChangesAsync();

        var handler = new ImportCbhpmGeralCommandHandler(
            context,
            NullLogger<ImportCbhpmGeralCommandHandler>.Instance);

        var result = await handler.Handle(new ImportCbhpmGeralCommand
        {
            Items =
            [
                new CbhpmImportItemDto
                {
                    Codigo = "1.01.01.01-2",
                    Procedimento = "Em consultorio",
                    Porte = "2B"
                },
                new CbhpmImportItemDto
                {
                    Codigo = "2.01.01.20-1",
                    Procedimento = "Avaliacao clinica e eletronica",
                    Porte = "2B",
                    CustoOperacional = 6.000m
                }
            ]
        }, CancellationToken.None);

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(1, result.InsertedItems);
        Assert.Equal(1, result.UpdatedItems);

        var storedItems = await context.CbhpmGeral.OrderBy(item => item.Codigo).ToListAsync();
        Assert.Equal(2, storedItems.Count);
        Assert.Equal("Em consultorio", storedItems[0].Procedimento);
        Assert.Equal("2B", storedItems[0].Porte);
        Assert.Equal(6.000m, storedItems[1].CustoOperacional);
    }

    [Fact]
    public async Task GetCbhpmGeral_FiltersAndPaginates()
    {
        await using var context = TestDbContextFactory.Create();
        context.CbhpmGeral.AddRange(
            new CbhpmGeral
            {
                Codigo = "1.01.01.01-2",
                Procedimento = "Em consultorio",
                Porte = "2B",
                Grupo = "CONSULTAS"
            },
            new CbhpmGeral
            {
                Codigo = "2.01.01.20-1",
                Procedimento = "Avaliacao clinica e eletronica de paciente portador de marca-passo",
                Porte = "2B",
                CustoOperacional = 6.000m
            },
            new CbhpmGeral
            {
                Codigo = "2.01.03.14-0",
                Procedimento = "Bloqueio fenolico, alcoolico ou com toxina botulinica",
                Porte = "4A",
                CustoOperacional = 1.950m
            });
        await context.SaveChangesAsync();

        var handler = new GetCbhpmGeralQueryHandler(
            context,
            NullLogger<GetCbhpmGeralQueryHandler>.Instance);

        var result = await handler.Handle(new GetCbhpmGeralQuery
        {
            Page = 1,
            PageSize = 1,
            Procedimento = "Avaliacao",
            Porte = "2B"
        }, CancellationToken.None);

        Assert.Equal(1, result.TotalItems);
        Assert.Equal(1, result.TotalPages);
        Assert.Single(result.Items);
        Assert.Equal("2.01.01.20-1", result.Items[0].Codigo);
        Assert.Equal(6.000m, result.Items[0].CustoOperacional);
    }

    [Fact]
    public async Task GetCbhpmGeral_FiltersProcedimentoByGrupo()
    {
        await using var context = TestDbContextFactory.Create();
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Em consultorio",
            Porte = "2B",
            Grupo = "CONSULTAS"
        });
        await context.SaveChangesAsync();

        var handler = new GetCbhpmGeralQueryHandler(
            context,
            NullLogger<GetCbhpmGeralQueryHandler>.Instance);

        var result = await handler.Handle(new GetCbhpmGeralQuery
        {
            Codigo = "1.01",
            Procedimento = "Consulta",
            Porte = "2B"
        }, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("1.01.01.01-2", result.Items[0].Codigo);
    }
}
