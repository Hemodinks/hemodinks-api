using HemodinksAPI.Application.Features.Clinics.Platform;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class CnpjUtilsTests
{
    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    [InlineData("  11.222.333/0001-81  ")]
    public void IsValid_AcceptsValidMaskedAndUnmaskedValues(string value)
    {
        Assert.True(CnpjUtils.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11.111.111/1111-11")]
    [InlineData("11.222.333/0001-82")]
    [InlineData("11.222.333/0001-A1")]
    [InlineData("1122233300018")]
    public void IsValid_RejectsInvalidValues(string? value)
    {
        Assert.False(CnpjUtils.IsValid(value));
    }

    [Fact]
    public void Normalize_RemovesFormattingAndKeepsFourteenDigits()
    {
        Assert.Equal("11222333000181", CnpjUtils.Normalize("11.222.333/0001-81"));
    }

    [Fact]
    public void RequestValidators_RequireCnpjOnCreateAndValidateItWhenUpdating()
    {
        var createRequest = new CreateClinicaRequest(
            "Clinica", "clinica", "", "Admin", "admin@example.com", "Senha@123",
            null, null, null, null, null, null, null, null, null);
        var updateRequest = new UpdateClinicaRequest(
            null, null, "12.345.678/0001-00", null, null, null, null,
            null, null, null, null, null, null);

        Assert.False(new CreateClinicaRequestValidator().Validate(createRequest).IsValid);
        Assert.False(new UpdateClinicaRequestValidator().Validate(updateRequest).IsValid);
    }

    [Fact]
    public void ClinicModel_HasAUniqueFilteredIndexForCnpj()
    {
        using var context = TestDbContextFactory.Create();
        var clinicType = context.Model.FindEntityType(typeof(Clinica));

        var cnpjIndex = Assert.Single(
            clinicType!.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(Clinica.Cnpj)]));

        Assert.True(cnpjIndex.IsUnique);
        Assert.Equal("[Cnpj] IS NOT NULL", cnpjIndex.GetFilter());
    }
}
