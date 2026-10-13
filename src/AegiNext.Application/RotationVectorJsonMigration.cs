using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class RotationVectorJsonMigration
{
    internal static void RejectUndeclaredFields(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value["property"] is JsonValue property &&
                (property.TryGetValue<string>(out var name) && Enum.TryParse<AnimationProperty>(name, true, out var parsed) &&
                 parsed == AnimationProperty.ROTATION_3D || property.TryGetValue<int>(out var number) && number == 39))
            {
                throw new JsonException("Projects older than version 17 cannot declare vector rotation tracks.");
            }
            if (value["transforms"] is JsonArray operations &&
                operations.OfType<JsonObject>().Any(operation => operation.ContainsKey("curve")))
            {
                throw new JsonException("Projects older than version 17 cannot declare ordered component curves.");
            }
            foreach (var item in value)
            {
                RejectUndeclaredFields(item.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RejectUndeclaredFields(item);
            }
        }
    }
}
