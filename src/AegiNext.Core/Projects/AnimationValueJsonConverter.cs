using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>为工程文件和媒体 worker 共用的标量、二维向量和线性 RGBA 编码。</summary>
public sealed class AnimationValueJsonConverter : JsonConverter<AnimationValue>
{
    /// <inheritdoc />
    public override AnimationValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var scalar))
        {
            return ReadNumber(scalar);
        }

        using var parsed = JsonDocument.ParseValue(ref reader);
        var value = parsed.RootElement;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.EnumerateObject().Count() == 2 && value.TryGetProperty("x", out _) && value.TryGetProperty("y", out _))
            {
                return new ScenePoint(ReadComponent(value, "x"), ReadComponent(value, "y"));
            }

            if (value.EnumerateObject().Count() == 4 && value.TryGetProperty("red", out _) &&
                value.TryGetProperty("green", out _) && value.TryGetProperty("blue", out _) && value.TryGetProperty("alpha", out _))
            {
                return new SceneColor(ReadComponent(value, "red"), ReadComponent(value, "green"),
                    ReadComponent(value, "blue"), ReadComponent(value, "alpha"));
            }
        }

        throw new JsonException("Animation values must be finite numbers, complete two-dimensional vectors, or complete linear RGBA colors.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AnimationValue value, JsonSerializerOptions options)
    {
        if (value.Kind == AnimationValueKind.SCALAR)
        {
            writer.WriteNumberValue(value.Scalar);
            return;
        }

        writer.WriteStartObject();
        if (value.Kind == AnimationValueKind.VECTOR)
        {
            writer.WriteNumber("x", value.Vector.X);
            writer.WriteNumber("y", value.Vector.Y);
        }
        else if (value.Kind == AnimationValueKind.COLOR)
        {
            writer.WriteNumber("red", value.Color.Red);
            writer.WriteNumber("green", value.Color.Green);
            writer.WriteNumber("blue", value.Color.Blue);
            writer.WriteNumber("alpha", value.Color.Alpha);
        }
        else
        {
            throw new JsonException("Unknown animation value kind.");
        }

        writer.WriteEndObject();
    }

    private static double ReadComponent(JsonElement value, string name)
    {
        var component = value.GetProperty(name);
        if (component.ValueKind != JsonValueKind.Number || !component.TryGetDouble(out var number))
        {
            throw new JsonException("Animation components must be numeric.");
        }

        return ReadNumber(number);
    }

    private static double ReadNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new JsonException("Animation values must be finite numbers.");
        }

        return value;
    }
}
