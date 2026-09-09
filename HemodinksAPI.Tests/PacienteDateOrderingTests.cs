using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class PacienteDateOrderingTests
{
    [Theory]
    [InlineData("data", "asc")]
    [InlineData("data", "desc")]
    [InlineData("dataAtendimento", "asc")]
    [InlineData("dataAtendimento", "desc")]
    public async Task DatesAreSortedBeforePaginationWithMissingDatesLast(string field, string direction)
    {
        await using var context = TestDbContextFactory.Create();
        DateTime?[] dates = [new(2030, 12, 1), null, new(2026, 12, 31), new(2027, 1, 2), new(2027, 1, 2)];
        for (var index = 0; index < dates.Length; index++)
        {
            var date = dates[index];
            context.Pacientes.Add(new Paciente
            {
                Id = index + 1, NomePaciente = $"Paciente {index}",
                Data = field == "data" ? date : new DateTime(2020, 1, 1).AddDays(index),
                DataAtendimento = field == "dataAtendimento" ? date : new DateTime(2020, 1, 1).AddDays(index),
                User = new User
                {
                    Nome = $"Paciente {index}", Email = $"paciente{index}@example.com", Telefone = "+5511999999999",
                    Senha = "test-hash", PerfilId = Perfil.PacientesId, DataCadastro = new DateTime(2026, 1, 1).AddDays(index)
                }
            });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var handler = new GetAllPacientesQueryHandler(context, NullLogger<GetAllPacientesQueryHandler>.Instance);
        var orderedIds = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await handler.Handle(new GetAllPacientesQuery
            {
                SortBy = field, SortDirection = direction, Page = page, PageSize = 2, CurrentPerfilId = Perfil.AdministradorId
            }, CancellationToken.None);
            Assert.Equal(5, result.TotalItems);
            orderedIds.AddRange(result.Items.Select(item => item.Id));
        }
        Assert.Equal(direction == "asc" ? [3, 4, 5, 1, 2] : new[] { 1, 5, 4, 3, 2 }, orderedIds);
    }
}
