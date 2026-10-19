using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleContentPersistenceTests
{
    [Fact]
    public void RealVersionThreeFieldsUpgradeWithoutChangingContentClock()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(3), new(5), "字😀");
        editor.UpdateSubtitle(id, line => line with { Karaoke = [new(0, 1, new(0), new(4, 3), SceneColor.White)] });
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        node["version"] = 3;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(node);
        var line = node["subtitles"]![0]!.AsObject();
        line.Remove("inlineSpans");
        line["style"]!.AsObject().Remove("underline");
        line["style"]!.AsObject().Remove("strikethrough");
        var clip = line["karaoke"]![0]!.AsObject();
        foreach (var key in new[] { "id", "highlightKind", "inactiveStyle", "activeStyle" })
        {
            clip.Remove(key);
        }
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        var upgraded = ProjectStore.Deserialize(bytes);
        Assert.Equal(ProjectDocument.CURRENT_VERSION, upgraded.Version);
        Assert.Equal(3, node["version"]!.GetValue<int>());
        var result = upgraded.Subtitles[0];
        Assert.Empty(result.InlineSpans);
        Assert.Empty(result.InactiveKaraoke);
        Assert.False(result.Style.Underline);
        Assert.Equal(KaraokeHighlightKind.SWEEP, result.Karaoke[0].HighlightKind);
        Assert.NotEqual(Guid.Empty, result.Karaoke[0].Id);
        Assert.Equal(new MediaTime(4, 3), result.Karaoke[0].End);
        Assert.Equal(editor.Snapshot.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)),
            upgraded.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)));
        Assert.Equal(result.Karaoke.ToArray(), ProjectStore.Deserialize(ProjectStore.Serialize(upgraded)).Subtitles[0].Karaoke.ToArray());
    }

    [Fact]
    public void VersionFourRequiresNewFieldsAndKeepsRichDataAndClipIdentity()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(3), "ab");
        editor.ApplySubtitleInlineStyle(id, 0, 1, new() { Underline = true, Fill = new(4, -0.1, 2, 0.5) });
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 1, new(0), new(1, 3), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
            }],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 0 })]
        });
        var serialized = ProjectStore.Serialize(editor.Snapshot);
        var roundtrip = ProjectStore.Deserialize(serialized);
        Assert.Equal(editor.Snapshot.Subtitles[0].InlineSpans.ToArray(), roundtrip.Subtitles[0].InlineSpans.ToArray());
        Assert.Equal(editor.Snapshot.Subtitles[0].Karaoke.ToArray(), roundtrip.Subtitles[0].Karaoke.ToArray());
        var node = JsonNode.Parse(serialized)!;
        node["subtitles"]![0]!.AsObject().Remove("inlineSpans");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveKaraokeRoundTripPreservesExactParametersAndEnablement(bool mixed)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(3), new(5), "a😀b");
        var inactive = new KaraokeSegment(1, 2, new(7, 3), new(17, 3), new(4, -0.1, 2, 0.5))
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Saved HDR", new()
        {
            Fill = new(3, 0, 1), StrokeWidth = 5, ShadowOffset = new(2, -4), ShadowBlur = 3
        });
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = mixed ? [new(0, 1, new(0), new(1, 3), SceneColor.White)] : [],
            InactiveKaraoke = [inactive],
            KaraokeStyleSpans = [new(1, 2,
                new() { StrokeWidth = 3, ShadowOffset = new(4, -2), ShadowBlur = 5, ShadowColor = new(0, 0, 2, 0.6) },
                new() { StrokeWidth = 0, Fill = SceneColor.Transparent })],
            KaraokeStyle = highlight
        });
        var source = editor.Snapshot;
        var bytes = ProjectStore.Serialize(source);
        var node = JsonNode.Parse(bytes)!;
        Assert.Single(node["subtitles"]![0]!["inactiveKaraoke"]!.AsArray());
        var restored = ProjectStore.Deserialize(bytes);
        var result = Assert.Single(restored.Subtitles);
        Assert.Equal(source.Subtitles[0].Karaoke.ToArray(), result.Karaoke.ToArray());
        Assert.Equal(inactive, Assert.Single(result.InactiveKaraoke));
        Assert.Equal(highlight, result.KaraokeStyle);
        Assert.Equal(source.Subtitles[0].KaraokeStyleSpans.ToArray(), result.KaraokeStyleSpans.ToArray());
        Assert.Equal(mixed ? SubtitleContentKind.KARAOKE : SubtitleContentKind.PLAIN, result.ContentKind);
        Assert.Equal(source.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)),
            restored.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
    }

    [Fact]
    public void EmptyInactiveKaraokeIsOmittedWithoutMakingOtherFieldsOptional()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(1), "a");
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        var line = node["subtitles"]![0]!.AsObject();
        Assert.False(line.ContainsKey("inactiveKaraoke"));
        Assert.Empty(ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())).Subtitles[0].InactiveKaraoke);
        line.Remove("karaoke");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyProjectWithoutInactiveKaraokeLoadsAnEmptyBackup(int version)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "ab");
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)]
        });
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        node["version"] = version;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(node.AsObject());
        node["subtitles"]![0]!.AsObject().Remove("inactiveKaraoke");
        var result = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Empty(result.Subtitles[0].InactiveKaraoke);
        Assert.Equal(editor.Snapshot.Subtitles[0].Karaoke.ToArray(), result.Subtitles[0].Karaoke.ToArray());
        Assert.Equal(ProjectDocument.CURRENT_VERSION, result.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void MalformedInactiveKaraokeIsRejectedInsteadOfRestored(int invalidValue)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "a😀");
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 2, new(1), new(2), SceneColor.White)]
        });
        var node = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        var line = node["subtitles"]![0]!.AsObject();
        var clips = line["inactiveKaraoke"]!.AsArray();
        var clip = clips[0]!.AsObject();
        switch (invalidValue)
        {
            case 0:
                line["inactiveKaraoke"] = null;
                break;
            case 1:
                clips[0] = null;
                break;
            case 2:
                clip["id"] = JsonValue.Create(editor.Snapshot.Subtitles[0].Karaoke[0].Id);
                break;
            case 3:
                clip["utf16Start"] = 0;
                clip["utf16Length"] = 1;
                break;
            case 4:
                clip["utf16Length"] = 1;
                break;
            case 5:
                clip.Remove("id");
                break;
            default:
                clip["unexpected"] = true;
                break;
        }
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OldStyleLibrariesAreRejected(int version)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version},\"presets\":[]}}");
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(bytes));
    }

    [Fact]
    public void SplittingPreservesInlineAppearanceAndGivesSplitClipANewRightIdentity()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "abcd");
        editor.ApplySubtitleInlineStyle(id, 1, 2, new() { Bold = true });
        editor.UpdateSubtitle(id, line => line with { Karaoke = [new(0, 4, new(0), new(4), SceneColor.White)] });
        editor.SplitKaraokeClip(id, editor.Snapshot.Subtitles[0].Karaoke[0].Id, 2, new(2));
        var before = editor.Snapshot;
        var split = ProjectEditingOperations.SplitSubtitle(before, id, new(2), 2);
        Assert.Equal(new SubtitleInlineSpan(1, 1, new() { Bold = true }), Assert.Single(split.Subtitles[0].InlineSpans));
        Assert.Equal(new SubtitleInlineSpan(0, 1, new() { Bold = true }), Assert.Single(split.Subtitles[1].InlineSpans));
        Assert.Equal(before.Subtitles[0].Karaoke[0].Id, split.Subtitles[0].Karaoke[0].Id);
        Assert.NotEqual(split.Subtitles[0].Karaoke[0].Id, split.Subtitles[1].Karaoke[0].Id);
        var merged = ProjectEditingOperations.MergeSubtitles(split, split.Subtitles[0].Id, split.Subtitles[1].Id, "");
        Assert.Equal("abcd", merged.Subtitles[0].Text);
        Assert.Equal(new SubtitleInlineSpan(1, 2, new() { Bold = true }), Assert.Single(merged.Subtitles[0].InlineSpans));
    }

    [Fact]
    public void MergePreservesSecondBaseAndInlineAppearance()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "a");
        var second = editor.AddSubtitle(new(2), new(4), "bc");
        editor.ApplySubtitleStyle(second, new() { FontSize = 88, Fill = new(4, 2, 1), Italic = true });
        editor.ApplySubtitleInlineStyle(second, 1, 1, new() { Bold = true });
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second, "");
        var line = merged.Subtitles[0];
        Assert.Equal(64, line.Style.FontSize);
        Assert.Equal(88, line.InlineSpans[0].Style.ApplyTo(line.Style).FontSize);
        Assert.Equal(new SceneColor(4, 2, 1), line.InlineSpans[0].Style.ApplyTo(line.Style).Fill);
        Assert.True(line.InlineSpans[^1].Style.ApplyTo(line.Style).Bold);
        Assert.True(line.InlineSpans[^1].Style.ApplyTo(line.Style).Italic);
    }
}
