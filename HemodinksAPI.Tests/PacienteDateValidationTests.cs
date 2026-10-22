using System.Text.Json;
using HemodinksAPI.Application.Features.Pacientes.Commands;

namespace HemodinksAPI.Tests;

public sealed class PacienteDateValidationTests
{
    private const string Error = "A data da consolidação não pode ser anterior à data da solicitação.";

    [Theory]
    [InlineData("2026-10-23", "2026-10-22")]
    [InlineData("2026-10-23T00:01:00", "2026-10-22T23:59:00")]
    [InlineData("2026-10-23T00:01:00+14:00", "2026-10-22T23:59:00-12:00")]
    public void Commands_RejectRequestAfterConsolidation(string requestDate, string consolidationDate)
    {
        var json = Payload(requestDate, consolidationDate);
        var create = JsonSerializer.Deserialize<CreatePacienteCommand>(json)!;
        var update = JsonSerializer.Deserialize<UpdatePacienteCommand>(json)!;

        Assert.Equal(Error, Assert.Throws<InvalidOperationException>(() =>
            new CreatePacienteCommandValidator().Validate(create)).Message);
        Assert.Equal(Error, Assert.Throws<InvalidOperationException>(() =>
            new UpdatePacienteCommandValidator().Validate(update)).Message);
    }

    [Theory]
    [InlineData("2026-10-22", "2026-10-23")]
    [InlineData("2026-10-22", "2026-10-22")]
    [InlineData("2026-10-22T23:59:00", "2026-10-22T00:01:00")]
    [InlineData("2026-10-22T23:59:00-12:00", "2026-10-22T00:01:00+14:00")]
    [InlineData("2026-10-22T23:59:00-12:00", "2026-10-23T00:01:00+14:00")]
    [InlineData(null, "2026-10-22")]
    [InlineData("2026-10-22", null)]
    [InlineData(null, null)]
    public void Commands_AcceptOrderedEqualOrMissingCivilDates(string? requestDate, string? consolidationDate)
    {
        var json = Payload(requestDate, consolidationDate);
        new CreatePacienteCommandValidator().Validate(JsonSerializer.Deserialize<CreatePacienteCommand>(json)!);
        new UpdatePacienteCommandValidator().Validate(JsonSerializer.Deserialize<UpdatePacienteCommand>(json)!);
    }

    [Theory]
    [InlineData("2026-10-22T00:01:00+14:00")]
    [InlineData("2026-10-22T23:59:00-12:00")]
    [InlineData("2026-10-22T23:59:00Z")]
    public void Commands_PreserveCivilDateDuringDeserialization(string date)
    {
        var json = Payload(date, date);
        var create = JsonSerializer.Deserialize<CreatePacienteCommand>(json)!;
        var update = JsonSerializer.Deserialize<UpdatePacienteCommand>(json)!;

        Assert.Equal(new DateTime(2026, 10, 22), create.Data!.Value.Date);
        Assert.Equal(create.Data, create.DataAtendimento);
        Assert.Equal(create.Data, update.Data);
        Assert.Equal(create.Data, update.DataAtendimento);
    }

    private static string Payload(string? requestDate, string? consolidationDate) =>
        JsonSerializer.Serialize(new { Id = 1, NomePaciente = "Paciente sintético", Data = requestDate, DataAtendimento = consolidationDate });
}
