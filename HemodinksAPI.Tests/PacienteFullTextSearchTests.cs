using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public class PacienteFullTextSearchTests
{
    [Theory]
    [InlineData("81900000125", "81900000125")]
    [InlineData("3.10.05.25-0", "31005250")]
    [InlineData("um, 25, pacinete", "")]
    [InlineData("25, 56", "")]
    [InlineData("paciente 25", "")]
    public void StructuredDigits_AreUsedOnlyForAnExclusiveNumericSearch(string search, string expected)
    {
        Assert.Equal(expected, PacienteFilters.GetStructuredSearchDigits(search));
    }

    [Fact]
    public void ApplyFilters_UsesFullTextOnPacienteColumnsWithoutContainsOnFilteredNavigationSubqueries()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=translation-only;Integrated Security=true")
            .Options;
        using var context = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());

        var query = PacienteFilters.ApplyFilters(
            context.Pacientes.AsNoTracking(),
            "Arthur paciente",
            string.Empty,
            "Cirurgiao",
            "Convenio",
            "Procedimento",
            [],
            [],
            null,
            null,
            null,
            null,
            supportsFullTextSearch: true);

        var sql = query.ToQueryString();

        Assert.Contains("CONTAINS([p].[NomePaciente]", sql);
        Assert.Contains("CONTAINS([p].[Medico]", sql);
        Assert.Contains("CONTAINS([p].[Convenio]", sql);
        Assert.Contains("CONTAINS([p].[Procedimento]", sql);
        Assert.DoesNotContain("CONTAINS([h0].[Nome]", sql);
        Assert.DoesNotContain("CONTAINS([u0].[Nome]", sql);
        Assert.DoesNotContain("CONTAINS([c0].[DescricaoConvenio]", sql);
        Assert.DoesNotContain("CONTAINS([o0].[Fornecedor]", sql);
    }
}
