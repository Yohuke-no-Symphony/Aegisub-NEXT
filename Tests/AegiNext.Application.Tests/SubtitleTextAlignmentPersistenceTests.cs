using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTextAlignmentPersistenceTests
{
    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void ProjectTrackDefaultsAndPresetsRoundTripExplicitAlignment(SubtitleTextAlignment textAlign)
    {
        var style = new SubtitleStyle
        {
            TextAlign = textAlign,
            Alignment = TextAlignment.TOP_RIGHT,
            Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(17.125, -23.5) }
        };
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Aligned", style);
        var id = editor.AddSubtitle(new(0), new(3), "Long line\nx");
        editor.ApplySubtitleInlineStyle(id, 1, 2, new() { Bold = true });
        var source = editor.Snapshot;
        var bytes = ProjectStore.Serialize(source);
        var restored = ProjectStore.Deserialize(bytes);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Equal(style, restored.Tracks[0].DefaultStyle);
        Assert.Equal(style, restored.Subtitles[0].Style);
        Assert.Equal(source.Subtitles[0].InlineSpans.ToArray(), restored.Subtitles[0].InlineSpans.ToArray());
        Assert.Equal(textAlign.ToString(), JsonNode.Parse(bytes)!["subtitles"]![0]!["style"]!["textAlign"]!.GetValue<string>());

        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Aligned", style);
        var collection = new SubtitleStylePresetCollection { Presets = [preset] };
        var restoredPresets = SubtitleStylePresetMetadataStore.Deserialize(SubtitleStylePresetMetadataStore.Serialize(collection));
        Assert.Equal(collection.Version, restoredPresets.Version);
        Assert.Equal(preset, Assert.Single(restoredPresets.Presets));
    }

    [Theory]
    [InlineData(TextAlignment.TOP_LEFT)]
    [InlineData(TextAlignment.TOP_CENTER)]
    [InlineData(TextAlignment.TOP_RIGHT)]
    [InlineData(TextAlignment.MIDDLE_LEFT)]
    [InlineData(TextAlignment.MIDDLE_CENTER)]
    [InlineData(TextAlignment.MIDDLE_RIGHT)]
    [InlineData(TextAlignment.BOTTOM_LEFT)]
    [InlineData(TextAlignment.BOTTOM_CENTER)]
    [InlineData(TextAlignment.BOTTOM_RIGHT)]
    public void MissingOptionalAlignmentKeepsLegacyPlacementAndIsNotWritten(TextAlignment alignment)
    {
        var style = new SubtitleStyle { Alignment = alignment };
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Legacy", style);
        editor.AddSubtitle(new(0), new(3), "Long line\nx");
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        Assert.DoesNotContain("textAlign", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Equal(style, restored.Subtitles[0].Style);
        Assert.Equal(style, restored.Tracks[0].DefaultStyle);
        Assert.Null(restored.Subtitles[0].Style.TextAlign);

        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Legacy", style);
        var presetBytes = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] });
        Assert.DoesNotContain("textAlign", Encoding.UTF8.GetString(presetBytes), StringComparison.Ordinal);
        Assert.Equal(preset, Assert.Single(SubtitleStylePresetMetadataStore.Deserialize(presetBytes).Presets));
    }

    [Fact]
    public void ExplicitNullAlignmentLoadsAsLegacyAndIsOmittedOnResave()
    {
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Legacy", new());
        editor.AddSubtitle(new(0), new(3), "Long line\nx");
        var project = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        project["subtitles"]![0]!["style"]!["textAlign"] = null;
        project["tracks"]![0]!["defaultStyle"]!["textAlign"] = null;
        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString()));
        Assert.Null(restored.Subtitles[0].Style.TextAlign);
        Assert.Null(restored.Tracks[0].DefaultStyle!.TextAlign);
        Assert.DoesNotContain("textAlign", Encoding.UTF8.GetString(ProjectStore.Serialize(restored)), StringComparison.Ordinal);

        var presets = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Legacy", new())] }))!;
        presets["presets"]![0]!["style"]!["textAlign"] = null;
        var restoredPresets = SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(presets.ToJsonString()));
        Assert.Null(Assert.Single(restoredPresets.Presets).Style.TextAlign);
        Assert.DoesNotContain("textAlign", Encoding.UTF8.GetString(SubtitleStylePresetMetadataStore.Serialize(restoredPresets)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"JUSTIFY\"")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void BothStoresRejectInvalidAlignmentValues(string value)
    {
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Aligned", new());
        editor.AddSubtitle(new(0), new(3), "Long line\nx");
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        var subtitle = JsonNode.Parse(bytes)!;
        subtitle["subtitles"]![0]!["style"]!["textAlign"] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(subtitle.ToJsonString())));

        var track = JsonNode.Parse(bytes)!;
        track["tracks"]![0]!["defaultStyle"]!["textAlign"] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(track.ToJsonString())));

        var presets = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Aligned", new())] }))!;
        presets["presets"]![0]!["style"]!["textAlign"] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(presets.ToJsonString())));
    }

    [Fact]
    public void BothStoresRejectUndefinedNativeAlignmentBeforeWriting()
    {
        var style = new SubtitleStyle { TextAlign = (SubtitleTextAlignment)99 };
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(3), "Long line\nx");
        var source = editor.Snapshot;
        var invalid = source with { Subtitles = [source.Subtitles[0] with { Style = style }] };
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(invalid));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Invalid", style)] }));
    }
}
