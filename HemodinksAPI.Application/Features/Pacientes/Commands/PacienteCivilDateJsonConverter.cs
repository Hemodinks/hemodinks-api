using System.Text.Json;
using System.Text.Json.Serialization;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

// These patient fields describe civil dates; an offset must not shift their calendar day.
internal sealed class PacienteCivilDateJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().DateTime;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
