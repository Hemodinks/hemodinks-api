using HemodinksAPI.Api.Features.Cbhpm.Queries;
using HemodinksAPI.Api.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class CbhpmQueryHandlerTests
{
    [Fact]
    public async Task GetCbhpmGeral_FiltersAndPaginates()
    {
        await using var context = TestDbContextFactory.Create();
        context.CbhpmGeral.AddRange(
            new CbhpmGeral
            {
                Codigo = "1.01.01.01-2",
                Procedimento = "Em consultorio",
                Porte = "2B"
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
}
