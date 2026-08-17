using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public class PacienteFullTextSearchTests
{
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
