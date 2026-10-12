using System.Text.Json.Nodes;

namespace AegiNext.Application.Tests;

internal static class LegacySubtitleMarginsJsonFixture
{
    internal static void DowngradeProject(JsonObject root)
    {
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);
        LegacyKaraokeStyleJsonFixture.Downgrade(root);
        LegacyTrackJsonFixture.DowngradeProject(root);
        if (root["version"]!.GetValue<int>() >= 8)
        {
            return;
        }

        foreach (var line in root["subtitles"]!.AsArray().OfType<JsonObject>())
        {
            DowngradeStyle(line["style"]!.AsObject());
        }
        foreach (var track in root["subtitleTracks"]!.AsArray().OfType<JsonObject>())
        {
            if (track["defaultStyle"] is JsonObject style)
            {
                DowngradeStyle(style);
            }
        }
    }

    internal static void DowngradeLibrary(JsonObject root)
    {
        LegacySubtitleScalingJsonFixture.DowngradeLibrary(root);
        if (root["version"]!.GetValue<int>() < 6)
        {
            LegacySubtitleAppearanceJsonFixture.Downgrade(root);
        }
        if (root["version"]!.GetValue<int>() >= 5)
        {
            return;
        }
        foreach (var preset in root["presets"]!.AsArray().OfType<JsonObject>())
        {
            DowngradeStyle(preset["style"]!.AsObject());
        }
    }

    internal static void DowngradeStyle(JsonObject style)
    {
        var margins = style["margins"]!.AsObject();
        var value = margins["left"]!.GetValue<double>();
        Assert.Equal(value, margins["right"]!.GetValue<double>());
        Assert.Equal(value, margins["vertical"]!.GetValue<double>());
        style.Remove("margins");
        style["margin"] = value;
    }
}
