using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleAppearanceJsonMigration
{
    internal static void UpgradeProject(JsonObject document, int sourceVersion)
    {
        if (sourceVersion >= 10)
        {
            return;
        }
        foreach (var line in Objects(document["subtitles"]))
        {
            UpgradeStyle(line["style"] as JsonObject, false);
            foreach (var span in Objects(line["inlineSpans"]))
            {
                UpgradeStyle(span["style"] as JsonObject, true);
            }
            UpgradeVisualStyle(line["karaokeStyle"] as JsonObject, false);
            foreach (var segment in Objects(line["karaoke"]).Concat(Objects(line["inactiveKaraoke"])))
            {
                UpgradeVisualStyle(segment["inactiveStyle"] as JsonObject, true);
                UpgradeVisualStyle(segment["activeStyle"] as JsonObject, true);
            }
        }
        foreach (var track in Objects(document[sourceVersion >= 9 ? "tracks" : "subtitleTracks"]))
        {
            UpgradeStyle(track["defaultStyle"] as JsonObject, false);
        }
        RejectNewAnimationProperties(document);
    }

    internal static void UpgradeStyleLibrary(JsonObject collection, int sourceVersion)
    {
        if (sourceVersion >= 6)
        {
            return;
        }
        foreach (var preset in Objects(collection["presets"]))
        {
            UpgradeStyle(preset["style"] as JsonObject, false);
        }
        collection["version"] = SubtitleStylePresetCollection.CURRENT_VERSION;
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        return node is JsonArray array ? array.OfType<JsonObject>() : [];
    }

    private static void UpgradeStyle(JsonObject? style, bool isOverride)
    {
        if (style is null)
        {
            return;
        }
        RejectFields(style, "letterSpacing", "wrapMode", "fillBlur", "strokeBlur");
        style["letterSpacing"] = isOverride ? null : JsonValue.Create(0);
        style["fillBlur"] = isOverride ? null : JsonValue.Create(0);
        style["strokeBlur"] = isOverride ? null : JsonValue.Create(0);
        if (!isOverride)
        {
            style["wrapMode"] = nameof(SubtitleWrapMode.GRAPHEME);
        }
    }

    private static void UpgradeVisualStyle(JsonObject? style, bool isOverride)
    {
        if (style is null)
        {
            return;
        }
        RejectFields(style, "fillBlur", "strokeBlur");
        style["fillBlur"] = isOverride ? null : JsonValue.Create(0);
        style["strokeBlur"] = isOverride ? null : JsonValue.Create(0);
    }

    private static void RejectFields(JsonObject style, params string[] names)
    {
        if (names.Any(style.ContainsKey))
        {
            throw new JsonException("Legacy subtitle styles cannot contain letter-spacing, wrapping, or channel-blur fields not declared by their version.");
        }
    }

    private static void RejectNewAnimationProperties(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value["property"] is JsonValue property && property.TryGetValue<string>(out var name) &&
                Enum.TryParse<AnimationProperty>(name, true, out var parsed) && AnimationPropertyMetadata.IsSubtitleOnlyProperty(parsed))
            {
                throw new JsonException("Legacy projects cannot contain subtitle letter-spacing or channel-blur animation not declared by their version.");
            }
            foreach (var item in value)
            {
                RejectNewAnimationProperties(item.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RejectNewAnimationProperties(item);
            }
        }
    }
}
