using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class NativeKaraokePersistenceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void LegacyGroupsKeepEqualPerGraphemePlaybackAndMoveBothVisualStatesToRanges(int version)
    {
        var source = Document();
        var root = JsonNode.Parse(ProjectStore.Serialize(source))!.AsObject();
        if (version < 5)
        {
            LegacyProjectJsonFixture.Downgrade(root, version);
        }
        else
        {
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        var upgraded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var line = upgraded.Subtitles[0];
        Assert.Equal(ProjectDocument.CURRENT_VERSION, upgraded.Version);
        Assert.Equal(2, line.Karaoke.Length);
        Assert.Equal(source.Subtitles[0].Karaoke[0].Id, line.Karaoke[0].Id);
        Assert.NotEqual(line.Karaoke[0].Id, line.Karaoke[1].Id);
        Assert.Equal(new MediaTime(1, 3), line.Karaoke[0].Start);
        Assert.Equal(new MediaTime(5, 6), line.Karaoke[0].End);
        Assert.Equal(new MediaTime(5, 6), line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(4, 3), line.Karaoke[1].End);
        Assert.Equal(source.Subtitles[0].KaraokeStyleSpans.ToArray(), line.KaraokeStyleSpans.ToArray());
        var saved = JsonNode.Parse(ProjectStore.Serialize(upgraded))!;
        var segment = saved["subtitles"]![0]!["karaoke"]![0]!.AsObject();
        Assert.False(segment.ContainsKey("activeStyle"));
        Assert.False(segment.ContainsKey("inactiveStyle"));
        Assert.Equal(ProjectStore.Serialize(upgraded), ProjectStore.Serialize(ProjectStore.Deserialize(ProjectStore.Serialize(upgraded))));
    }

    [Fact]
    public void CurrentVersionRoundTripPreservesMultiCharacterGroupsGapsAndDormantStyles()
    {
        var document = Document();
        var line = document.Subtitles[0];
        line = line with
        {
            Text = line.Text + "B", InactiveKaraoke = [new(3, 1, new(3), new(4), SceneColor.White)],
            KaraokeStyleSpans = line.KaraokeStyleSpans.Add(new(3, 1, null, new() { Fill = SceneColor.Transparent }))
        };
        document = document with { Subtitles = [line] };
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(document)).Subtitles[0];
        Assert.Equal(line.Karaoke.ToArray(), restored.Karaoke.ToArray());
        Assert.Equal(line.InactiveKaraoke.ToArray(), restored.InactiveKaraoke.ToArray());
        Assert.Equal(line.KaraokeStyleSpans.ToArray(), restored.KaraokeStyleSpans.ToArray());
        Assert.Single(restored.Karaoke);
    }

    [Fact]
    public void CurrentVersionRejectsOldClipVisualFieldsAndLegacyRejectsUnversionedRangeFields()
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(Document()))!.AsObject();
        root["subtitles"]![0]!["karaoke"]![0]!["activeStyle"] = null;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        root = JsonNode.Parse(ProjectStore.Serialize(Document()))!.AsObject();
        root["version"] = 10;
        LegacySubtitleScalingJsonFixture.DowngradeProject(root);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "Ae\u0301", Karaoke = [new(0, 3, new(1, 3), new(4, 3), new(4.123456789123, 0.25, 1))],
            KaraokeStyleSpans = [new(0, 3,
                new() { Fill = SceneColor.Transparent, ShadowOffset = new(3.123456789123, -2.234567891234) },
                new() { StrokeWidth = 0, Fill = SceneColor.Black })]
        };
        return new() { Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
