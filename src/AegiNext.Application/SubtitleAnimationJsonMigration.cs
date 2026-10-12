using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleAnimationJsonMigration
{
    internal static void RejectScopedTransformFields(JsonNode? node, string? context = null)
    {
        if (node is JsonObject value)
        {
            if (context == "animationRanges" && (value.ContainsKey("offset") || value.ContainsKey("generatedOrigin")) ||
                context is "keyframes" or "componentCurves" && value.ContainsKey("reverse"))
            {
                throw new JsonException("Legacy projects cannot contain range offsets, generated origins, or reversed curves not declared by their version.");
            }
            if (context == "target" && value["textRangeId"] is not null &&
                value["property"] is JsonValue property && property.TryGetValue<string>(out var name) &&
                Enum.TryParse<AnimationProperty>(name, true, out var parsed) && parsed == AnimationProperty.POSITION)
            {
                throw new JsonException("Legacy projects cannot contain text-range offset animation not declared by their version.");
            }
            foreach (var item in value)
            {
                RejectScopedTransformFields(item.Value, item.Key);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RejectScopedTransformFields(item, context);
            }
        }
    }

    internal static void RejectUndeclaredFields(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("animationRanges") || value.ContainsKey("textRangeId") || value.ContainsKey("state") ||
                value.ContainsKey("colorSpace") || value.ContainsKey("componentMask") || value.ContainsKey("mode"))
            {
                throw new JsonException("Legacy projects cannot contain text ranges, states, or interpolation modes not declared by their version.");
            }
            if (value["property"] is JsonValue property && property.TryGetValue<string>(out var name) &&
                Enum.TryParse<AnimationProperty>(name, true, out var parsed) &&
                parsed is AnimationProperty.FONT_SIZE or AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or AnimationProperty.SHADOW_COLOR)
            {
                throw new JsonException("Legacy projects cannot contain font-size or shadow animation not declared by their version.");
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
