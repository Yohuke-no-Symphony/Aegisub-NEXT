using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SpatialTransformJsonMigration
{
    internal static void RejectUndeclaredFields(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("rotationX") || value.ContainsKey("rotationY") ||
                value.ContainsKey("perspectiveDistance") || value.ContainsKey("projectionTranslation"))
            {
                throw new JsonException("Projects older than version 16 cannot declare three-dimensional transforms.");
            }
            if (value["property"] is JsonValue property &&
                (property.TryGetValue<string>(out var name) && Enum.TryParse<AnimationProperty>(name, true, out var parsed) &&
                 parsed is AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.PERSPECTIVE_DISTANCE ||
                 property.TryGetValue<int>(out var number) && number is >= 36 and <= 38))
            {
                throw new JsonException("Projects older than version 16 cannot declare three-dimensional animation tracks.");
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
