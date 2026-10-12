using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class VectorAnimationJsonMigration
{
    internal static JsonObject Upgrade(JsonElement root, JsonSerializerOptions options)
    {
        var document = JsonNode.Parse(root.GetRawText(), documentOptions: new() { MaxDepth = 128 })!.AsObject();
        var styles = new Dictionary<Guid, SubtitleStyle>();
        if (document["subtitles"] is JsonArray subtitles)
        {
            foreach (var line in subtitles.OfType<JsonObject>())
            {
                if (line["id"] is JsonValue id && id.TryGetValue<Guid>(out var subtitleId) && line["style"] is JsonObject style)
                {
                    if (!styles.TryAdd(subtitleId, style.Deserialize<SubtitleStyle>(options) ?? throw new JsonException("The subtitle style cannot be null.")))
                    {
                        throw new JsonException("Duplicate subtitle ID.");
                    }
                }
            }
        }

        if (document["layers"] is JsonArray layers)
        {
            foreach (var layer in layers.OfType<JsonObject>())
            {
                UpgradeLayer(layer, styles, options);
            }
        }

        if (document["presets"] is JsonArray presets)
        {
            foreach (var preset in presets.OfType<JsonObject>())
            {
                UpgradeTracks(preset, new(), null, null, options);
            }
        }

        return document;
    }

    private static void UpgradeLayer(JsonObject layer, Dictionary<Guid, SubtitleStyle> styles, JsonSerializerOptions options)
    {
        if (layer["transform"] is JsonObject transform)
        {
            if (transform.ContainsKey("x") || transform.ContainsKey("scaleX"))
            {
                var expected = new HashSet<string>(["x", "y", "scaleX", "scaleY", "rotation", "anchorX", "anchorY"], StringComparer.Ordinal);
                if (transform.Count != expected.Count || transform.Any(pair => !expected.Contains(pair.Key)))
                {
                    throw new JsonException("Legacy layer-transform fields are missing, or both old and new vector fields are present.");
                }

                layer["transform"] = new JsonObject
                {
                    ["position"] = Point(ReadNumber(transform, "x"), ReadNumber(transform, "y")),
                    ["scale"] = Point(ReadNumber(transform, "scaleX"), ReadNumber(transform, "scaleY")),
                    ["pivot"] = Point(ReadNumber(transform, "anchorX"), ReadNumber(transform, "anchorY")),
                    ["rotation"] = ReadNumber(transform, "rotation")
                };
            }

            var value = layer["transform"]!.Deserialize<LayerTransform>(options) ?? throw new JsonException("The layer transform cannot be null.");
            var hasLegacyColor = layer["tracks"] is JsonArray tracks && tracks.OfType<JsonObject>().Any(track =>
                track["target"]?["property"] is JsonValue property && property.TryGetValue<string>(out var name) &&
                Enum.TryParse<AnimationProperty>(name, ignoreCase: true, out var animationProperty) &&
                animationProperty is >= AnimationProperty.FILL_RED and <= AnimationProperty.STROKE_ALPHA);
            SceneColor? fill = null;
            SceneColor? stroke = null;
            if (hasLegacyColor)
            {
                if (layer["subtitleId"] is JsonValue id && id.TryGetValue<Guid>(out var subtitleId))
                {
                    if (!styles.TryGetValue(subtitleId, out var style))
                    {
                        throw new JsonException("A legacy subtitle color track is missing its referenced subtitle style.");
                    }

                    fill = style.Fill;
                    stroke = style.Stroke;
                }
                else
                {
                    fill = layer["fill"]?.Deserialize<SceneColor>(options) ?? throw new JsonException("A legacy color track is missing the layer fill.");
                    stroke = layer["stroke"]?.Deserialize<SceneColor>(options) ?? throw new JsonException("A legacy color track is missing the layer stroke.");
                }
            }

            UpgradeTracks(layer, value, fill, stroke, options);
        }

        if (layer["children"] is JsonArray children)
        {
            foreach (var child in children.OfType<JsonObject>())
            {
                UpgradeLayer(child, styles, options);
            }
        }
    }

    private static JsonObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };

    private static double ReadNumber(JsonObject value, string name)
    {
        if (value[name] is not JsonValue number || !number.TryGetValue<double>(out var result) || !double.IsFinite(result))
        {
            throw new JsonException($"Legacy transform {name} is not a valid number.");
        }

        return result;
    }

    private static void UpgradeTracks(JsonObject owner, LayerTransform transform, SceneColor? fill, SceneColor? stroke,
        JsonSerializerOptions options)
    {
        if (owner["tracks"] is not JsonArray nodes)
        {
            return;
        }

        foreach (var key in nodes.OfType<JsonObject>().SelectMany(track => (track["keyframes"] as JsonArray ?? []).OfType<JsonObject>()))
        {
            if (key.ContainsKey("vectorCurve"))
            {
                if (key.ContainsKey("componentCurves"))
                {
                    throw new JsonException("A keyframe contains both old and new component-curve fields.");
                }

                key["componentCurves"] = key["vectorCurve"] is { } curve ? new JsonArray(curve.DeepClone()) : new JsonArray();
                key.Remove("vectorCurve");
            }
        }

        var tracks = nodes.Deserialize<ImmutableArray<AnimationTrack>>(options);
        if (tracks.IsDefault || tracks.Any(track => track is null))
        {
            throw new JsonException("The animation track collection contains a null item.");
        }

        var result = LegacyAnimationTrackMigration.Merge(tracks, AnimationProperty.POSITION, transform.Position);
        result = LegacyAnimationTrackMigration.Merge(result, AnimationProperty.SCALE, transform.Scale);
        if (fill is { } fillValue)
        {
            result = LegacyAnimationTrackMigration.Merge(result, AnimationProperty.FILL, fillValue);
        }

        if (stroke is { } strokeValue)
        {
            result = LegacyAnimationTrackMigration.Merge(result, AnimationProperty.STROKE, strokeValue);
        }

        if (result != tracks)
        {
            owner["tracks"] = JsonSerializer.SerializeToNode(result, options);
        }
    }
}
