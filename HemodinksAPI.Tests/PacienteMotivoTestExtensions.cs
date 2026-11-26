using System.Net.Http.Json;

namespace HemodinksAPI.Tests;

// Inativar e reativar paciente exigem motivo no corpo (ate 500 caracteres).
internal static class PacienteMotivoTestExtensions
{
    public const string MotivoPadrao = "Motivo informado no teste";

    public static Task<HttpResponseMessage> InativarPacienteAsync(this HttpClient client, int pacienteId,
        string? motivo = MotivoPadrao) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/pacientes/{pacienteId}")
        {
            Content = JsonContent.Create(new { motivo })
        });

    public static Task<HttpResponseMessage> ReativarPacienteAsync(this HttpClient client, int pacienteId,
        string? motivo = MotivoPadrao) =>
        client.PostAsJsonAsync($"/api/pacientes/{pacienteId}/reativacao", new { motivo });
}
