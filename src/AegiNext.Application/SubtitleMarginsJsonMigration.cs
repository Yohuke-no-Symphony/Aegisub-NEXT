using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Presets;

namespace AegiNext.Application;

internal static class SubtitleMarginsJsonMigration
{
    private const int PROJECT_MARGINS_VERSION = 8;
    private const int STYLE_LIBRARY_MARGINS_VERSION = 5;

    internal static void UpgradeProject(JsonObject document, int sourceVersion)
    {
        if (sourceVersion >= PROJECT_MARGINS_VERSION)
        {
            return;
        }
        UpgradeStyles(document["subtitles"] as JsonArray, "style");
        UpgradeStyles(document["subtitleTracks"] as JsonArray, "defaultStyle");
    }

    internal static void UpgradeStyleLibrary(JsonObject collection, int sourceVersion)
    {
        if (sourceVersion >= STYLE_LIBRARY_MARGINS_VERSION)
        {
            return;
        }
        UpgradeStyles(collection["presets"] as JsonArray, "style");
        collection["version"] = SubtitleStylePresetCollection.CURRENT_VERSION;
    }

    private static void UpgradeStyles(JsonArray? items, string styleName)
    {
        if (items is null)
        {
            return;
        }
        foreach (var item in items.OfType<JsonObject>())
        {
            if (item[styleName] is JsonObject style)
            {
                UpgradeStyle(style);
            }
        }
    }

    private static void UpgradeStyle(JsonObject style)
    {
        if (style.ContainsKey("margins"))
        {
            throw new JsonException("Legacy subtitle styles cannot contain separate margin fields not declared by their version.");
        }
        if (style["margin"] is not JsonValue value || !value.TryGetValue<double>(out var margin) ||
            !double.IsFinite(margin) || margin is < 0 or > 32768)
        {
            throw new JsonException("Legacy subtitle styles must contain complete, valid scalar margins.");
        }
        style.Remove("margin");
        style["margins"] = new JsonObject
        {
            ["left"] = margin, ["right"] = margin, ["vertical"] = margin
        };
    }
}
