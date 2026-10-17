using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turn_One_Link.Services.Telemetry;

/// <summary>
/// Serializes <see cref="T1Corners"/> as a 4-element `[fl, fr, rl, rr]` JSON array, per the
/// v2 cloud contract (docs/telemetry/backend_contract_v2.md §3 — "Arrays are flattened by
/// the backend to _fl/_fr/_rl/_rr fields"). Only used for the v2 cloud stream; the local
/// mobile protocol (t1_protocol.md) is untouched and keeps its own object shape.
/// </summary>
public sealed class T1CornersJsonConverter : JsonConverter<T1Corners>
{
    public override T1Corners Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected a 4-element array for T1Corners.");

        Span<float> values = stackalloc float[4];
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (i < 4) values[i] = reader.GetSingle();
            i++;
        }

        return new T1Corners(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, T1Corners value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Fl);
        writer.WriteNumberValue(value.Fr);
        writer.WriteNumberValue(value.Rl);
        writer.WriteNumberValue(value.Rr);
        writer.WriteEndArray();
    }
}
