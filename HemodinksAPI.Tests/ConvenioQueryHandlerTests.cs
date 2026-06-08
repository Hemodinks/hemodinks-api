using HemodinksAPI.Api.Features.Convenios.Queries;

namespace HemodinksAPI.Tests;

public class ConvenioQueryHandlerTests
{
    [Fact]
    public async Task GetConvenios_ReturnsSeededConveniosInAlphabeticalOrder()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new GetConveniosQueryHandler(context);

        var result = await handler.Handle(new GetConveniosQuery(), CancellationToken.None);

        Assert.Equal(9, result.Count);
        Assert.Equal(
            [
                "Amil",
                "Bradesco Sa\u00fade",
                "Cemig Sa\u00fade",
                "Fusex",
                "Geap",
                "Ipsemg",
                "Particular",
                "Sul Am\u00e9rica",
                "Unimed Uberl\u00e2ndia - Plano  Unimed Interc\u00e2mbio"
            ],
            result.Select(convenio => convenio.DescricaoConvenio));
    }
}
