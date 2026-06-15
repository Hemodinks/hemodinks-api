using HemodinksAPI.Application.Features.Opme.Queries;

namespace HemodinksAPI.Tests;

public class OpmeQueryHandlerTests
{
    [Fact]
    public async Task GetOpme_ReturnsSeededFornecedoresInAlphabeticalOrder()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new GetOpmeQueryHandler(context);

        var result = await handler.Handle(new GetOpmeQuery(), CancellationToken.None);

        Assert.Equal(
            [
                "AVL",
                "GE",
                "Promedom",
                "Spyner"
            ],
            result.Select(item => item.Fornecedor));
    }
}
