using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Presets;

namespace AegiNext.Application;

internal static class SubtitleScalingJsonMigration
{
    private const int PROJECT_SCALING_VERSION = 15;
    private const int STYLE_LIBRARY_SCALING_VERSION = 7;
    private const int LEGACY_LIBRARY_REFERENCE_WIDTH = 1920;
    private const int LEGACY_LIBRARY_REFERENCE_HEIGHT = 1080;
    private const double LEGACY_MATCH_WIDTH_OR_HEIGHT = 0.5;

    internal static void UpgradeProject(JsonObject document, int sourceVersion)
    {
        if (sourceVersion >= PROJECT_SCALING_VERSION)
        {
            return;
        }
        var width = ReadDimension(document, "width");
        var height = ReadDimension(document, "height");
        UpgradeStyles(document["subtitles"] as JsonArray, "style", width, height);
        UpgradeStyles(document[sourceVersion >= 9 ? "tracks" : "subtitleTracks"] as JsonArray, "defaultStyle", width, height);
    }

    internal static void UpgradeStyleLibrary(JsonObject collection, int sourceVersion)
    {
        if (sourceVersion >= STYLE_LIBRARY_SCALING_VERSION)
        {
            return;
        }
        UpgradeStyles(collection["presets"] as JsonArray, "style", LEGACY_LIBRARY_REFERENCE_WIDTH, LEGACY_LIBRARY_REFERENCE_HEIGHT);
        collection["version"] = SubtitleStylePresetCollection.CURRENT_VERSION;
    }

    private static int ReadDimension(JsonObject document, string name)
    {
        if (document[name] is not JsonValue value || !value.TryGetValue<int>(out var dimension) ||
            dimension is <= 0 or > 32768)
        {
            throw new JsonException("旧版工程必须包含完整、有效的画布尺寸。");
        }
        return dimension;
    }

    private static void UpgradeStyles(JsonArray? items, string styleName, int width, int height)
    {
        if (items is null)
        {
            return;
        }
        foreach (var item in items.OfType<JsonObject>())
        {
            if (item[styleName] is not JsonObject style)
            {
                continue;
            }
            if (style.ContainsKey("scaling"))
            {
                throw new JsonException("旧版字幕样式不能包含未声明版本的参考尺寸字段。");
            }
            style["scaling"] = new JsonObject
            {
                ["referenceWidth"] = width,
                ["referenceHeight"] = height,
                ["matchWidthOrHeight"] = LEGACY_MATCH_WIDTH_OR_HEIGHT
            };
        }
    }
}
