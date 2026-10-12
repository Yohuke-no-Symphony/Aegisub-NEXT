using System.Text.Json.Nodes;

namespace AegiNext.Application.Tests;

internal static class LegacySubtitleScalingJsonFixture
{
    internal static void DowngradeProject(JsonObject root)
    {
        if (root["version"]!.GetValue<int>() >= 15)
        {
            return;
        }
        DowngradeStyles(root["subtitles"] as JsonArray, "style");
        DowngradeStyles(root["tracks"] as JsonArray, "defaultStyle");
        DowngradeStyles(root["subtitleTracks"] as JsonArray, "defaultStyle");
    }

    internal static void DowngradeLibrary(JsonObject root)
    {
        if (root["version"]!.GetValue<int>() < 7)
        {
            DowngradeStyles(root["presets"] as JsonArray, "style");
        }
    }

    private static void DowngradeStyles(JsonArray? items, string styleName)
    {
        if (items is null)
        {
            return;
        }
        foreach (var item in items.OfType<JsonObject>())
        {
            if (item[styleName] is JsonObject style)
            {
                style.Remove("scaling");
            }
        }
    }
}
