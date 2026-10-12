using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class ClipMaskJsonMigration
{
    internal static void RejectCurrentLegacyFields(JsonElement document)
    {
        if (document.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            RejectLayerLegacyFields(layers);
        }

        if (document.TryGetProperty("presets", out var presets) && presets.ValueKind == JsonValueKind.Array)
        {
            foreach (var preset in presets.EnumerateArray())
            {
                if (preset.ValueKind == JsonValueKind.Object)
                {
                    RejectTrackLegacyFields(preset);
                }
            }
        }
    }

    internal static void UpgradeLegacy(JsonObject document)
    {
        if (document["layers"] is JsonArray layers)
        {
            UpgradeLayers(layers, "layers");
        }

        if (document["presets"] is JsonArray presets)
        {
            for (var index = 0; index < presets.Count; index++)
            {
                if (presets[index] is not JsonObject preset)
                {
                    continue;
                }

                var location = $"presets[{index}]";
                RejectNonemptyMask(preset, location);
                preset.Remove("mask");
                UpgradeTracks(preset, location);
            }
        }

        document["version"] = ProjectDocument.CURRENT_VERSION;
    }

    private static void UpgradeLayers(JsonArray layers, string location)
    {
        for (var index = 0; index < layers.Count; index++)
        {
            if (layers[index] is not JsonObject layer)
            {
                continue;
            }

            var layerLocation = $"{location}[{index}]";
            RejectNonemptyMask(layer, layerLocation);
            layer.TryAdd("mask", null);
            UpgradeTracks(layer, layerLocation);
            if (layer["children"] is JsonArray children)
            {
                UpgradeLayers(children, $"{layerLocation}.children");
            }
        }
    }

    private static void RejectLayerLegacyFields(JsonElement layers)
    {
        foreach (var layer in layers.EnumerateArray())
        {
            if (layer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (layer.TryGetProperty("transform", out var transform) && transform.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in transform.EnumerateObject())
                {
                    if (property.NameEquals("x") || property.NameEquals("y") ||
                        property.NameEquals("scaleX") || property.NameEquals("scaleY") ||
                        property.NameEquals("anchorX") || property.NameEquals("anchorY"))
                    {
                        throw new JsonException("Version 5 does not accept legacy scalar layer-transform fields.");
                    }
                }
            }

            RejectTrackLegacyFields(layer);
            if (layer.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                RejectLayerLegacyFields(children);
            }
        }
    }

    private static void RejectTrackLegacyFields(JsonElement owner)
    {
        if (!owner.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (track.TryGetProperty("property", out _))
            {
                throw new JsonException("Version 5 animation tracks require a complete target and do not accept the legacy property field.");
            }

            if (track.TryGetProperty("keyframes", out var frames) && frames.ValueKind == JsonValueKind.Array)
            {
                foreach (var frame in frames.EnumerateArray())
                {
                    if (frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("vectorCurve", out _))
                    {
                        throw new JsonException("Version 5 keyframes do not accept the legacy vectorCurve field.");
                    }
                }
            }
        }
    }

    private static void RejectNonemptyMask(JsonObject owner, string location)
    {
        if (owner["mask"] is not null)
        {
            var id = owner["id"] is JsonValue value && value.TryGetValue<string>(out var identifier)
                ? identifier : "unknown ID";
            throw new InvalidDataException($"{location} ({id}) contains a nonempty legacy local mask that cannot be safely upgraded to a project-coordinate Clip mask; the original file was not modified.");
        }
    }

    private static void UpgradeTracks(JsonObject owner, string location)
    {
        if (owner["tracks"] is not JsonArray tracks)
        {
            return;
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            if (tracks[index] is not JsonObject track)
            {
                continue;
            }

            if (track.ContainsKey("target") || track["property"] is not JsonValue value ||
                !value.TryGetValue<string>(out var property))
            {
                throw new JsonException($"{location}.tracks[{index}] is missing its legacy property field or contains both old and new track targets.");
            }

            track["target"] = new JsonObject { ["property"] = property, ["nodeId"] = null };
            track.Remove("property");
        }
    }
}
