using System.Text.Json;
using HemodinksAPI.Api;

namespace HemodinksAPI.Tests;

public sealed class MonitoringLogReaderTests
{
    [Fact]
    public async Task Read_ReturnsOnlyErrorsFromRequestedClinicWithTechnicalFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hemodinks-monitoring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var lines = new[]
            {
                CreateEvent("Information", "1", "SELECT 1", null),
                CreateEvent(
                    "Error",
                    "1",
                    "SELECT [p].[Id] FROM [Pacientes] AS [p]",
                    "System.InvalidOperationException: Falha técnica\n   at HemodinksAPI.Application.Features.Pacientes.Queries.GetPaciente.Handle() in C:\\src\\GetPaciente.cs:line 57\n   at HemodinksAPI.Api.PacienteEndpointExtensions.Get() in C:\\src\\PacienteEndpointExtensions.cs:line 20"),
                CreateEvent("Error", "2", "DELETE FROM [Pacientes]", "System.Exception: Outra clínica")
            };
            File.WriteAllLines(Path.Combine(directory, "hemodinks-errors-20260825.json"), lines);

            var result = new MonitoringLogReader(directory).Read(1, 25, clinicId: 1);

            var error = Assert.Single(result.Items);
            Assert.Equal("Pacientes", error.Module);
            Assert.Equal("Handle", error.Method);
            Assert.Equal(57, error.Line);
            Assert.Equal("SELECT", error.DatabaseOperation);
            Assert.Equal("George", error.UserName);
            Assert.Equal("george@example.com", error.UserEmail);
            Assert.Equal(2, error.ClassFlow.Count);
            Assert.Equal("System.InvalidOperationException: Falha técnica", error.TechnicalDescription);

            var clearedAt = await new MonitoringLogReader(directory).ClearAsync(1, CancellationToken.None);
            Assert.True(clearedAt <= DateTimeOffset.UtcNow);
            Assert.Empty(new MonitoringLogReader(directory).Read(1, 25, clinicId: 1).Items);

            var globalResult = new MonitoringLogReader(directory).Read(1, 25, clinicId: null);
            Assert.Single(globalResult.Items);
            Assert.Equal("System.Exception: Outra clínica", globalResult.Items[0].TechnicalDescription);

            await new MonitoringLogReader(directory).ClearAsync(null, CancellationToken.None);
            Assert.Empty(new MonitoringLogReader(directory).Read(1, 25, clinicId: null).Items);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateEvent(string level, string clinicId, string? commandText, string? exception)
    {
        return JsonSerializer.Serialize(new
        {
            Timestamp = "2020-08-25T18:45:00.0000000-03:00",
            Level = level,
            RenderedMessage = "Falha ao consultar pacientes",
            Exception = exception,
            Properties = new Dictionary<string, object?>
            {
                ["SourceContext"] = "HemodinksAPI.Application.Features.Pacientes.Queries.GetPaciente",
                ["ClinicId"] = clinicId,
                ["UserName"] = "George",
                ["UserEmail"] = "george@example.com",
                ["RequestId"] = "request-123",
                ["CommandText"] = commandText
            }
        });
    }
}
