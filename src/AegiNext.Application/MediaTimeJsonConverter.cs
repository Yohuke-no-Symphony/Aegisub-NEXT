using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal sealed class MediaTimeJsonConverter : JsonConverter<MediaTime>
{
    public override MediaTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        var element = value.RootElement;
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != 2 ||
            !element.TryGetProperty("numerator", out var numerator) || !numerator.TryGetInt64(out var n) ||
            !element.TryGetProperty("denominator", out var denominator) || !denominator.TryGetInt64(out var d) || d <= 0)
        {
            throw new JsonException("Time must be represented by an Int64 numerator and a positive denominator.");
        }

        return new(n, d);
    }

    public override void Write(Utf8JsonWriter writer, MediaTime value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("numerator", value.Numerator);
        writer.WriteNumber("denominator", value.Denominator);
        writer.WriteEndObject();
    }
}
