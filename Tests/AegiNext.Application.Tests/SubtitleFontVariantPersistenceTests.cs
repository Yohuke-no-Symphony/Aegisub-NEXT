using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleFontVariantPersistenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("ExampleSans-SemiBold")]
    public void ProjectPresetsAndTrackDefaultsRoundTripVariants(string? postScriptName)
    {
        var style = Style() with { FontVariant = Style().FontVariant!.Value with { PostScriptName = postScriptName } };
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Named face", style);
        var id = editor.AddSubtitle(new(0), new(2), "AB");
        editor.ApplySubtitleInlineStyle(id, 1, 1, new() { FontVariant = style.FontVariant!.Value with { Name = "Black", Weight = 900 } });
        var source = editor.Snapshot;
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(source));
        Assert.Equal(style, restored.Tracks[0].DefaultStyle);
        Assert.Equal(style, restored.Subtitles[0].Style);
        Assert.Equal(source.Subtitles[0].InlineSpans.ToArray(), restored.Subtitles[0].InlineSpans.ToArray());
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Named face", style);
        var collection = SubtitleStylePresetMetadataStore.Deserialize(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] }));
        Assert.Equal(preset, Assert.Single(collection.Presets));
    }

    [Fact]
    public void LegacyProjectAndPresetStylesDoNotRequireOrWriteNewFields()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "AB");
        editor.ApplySubtitleInlineStyle(id, 1, 1, new() { Bold = true });
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("fontVariant", json, StringComparison.Ordinal);
        Assert.DoesNotContain("clearFontVariant", json, StringComparison.Ordinal);
        Assert.Null(ProjectStore.Deserialize(bytes).Subtitles[0].Style.FontVariant);
        var presetBytes = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Old", new() { Bold = true })] });
        Assert.DoesNotContain("fontVariant", Encoding.UTF8.GetString(presetBytes), StringComparison.Ordinal);
        Assert.True(Assert.Single(SubtitleStylePresetMetadataStore.Deserialize(presetBytes).Presets).Style.Bold);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("weight")]
    [InlineData("width")]
    [InlineData("italic")]
    public void IncompleteVariantPayloadIsRejectedByBothStores(string field)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "AB");
        editor.ApplySubtitleStyle(id, Style());
        var project = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        project["subtitles"]![0]!["style"]!["fontVariant"]!.AsObject().Remove(field);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var presets = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Face", Style())] }))!;
        presets["presets"]![0]!["style"]!["fontVariant"]!.AsObject().Remove(field);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(presets.ToJsonString())));
    }

    [Fact]
    public void OptionalPostScriptNameCanBeOmittedAndIsNotWrittenWhenNull()
    {
        var style = Style();
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "AB");
        editor.ApplySubtitleStyle(id, style);
        var project = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        project["subtitles"]![0]!["style"]!["fontVariant"]!.AsObject().Remove("postScriptName");
        var restoredProject = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString()));
        Assert.Equal(style.FontVariant!.Value with { PostScriptName = null }, restoredProject.Subtitles[0].Style.FontVariant);
        Assert.DoesNotContain("postScriptName", Encoding.UTF8.GetString(ProjectStore.Serialize(restoredProject)), StringComparison.Ordinal);

        var presets = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [new(Guid.NewGuid(), "Face", style)] }))!;
        presets["presets"]![0]!["style"]!["fontVariant"]!.AsObject().Remove("postScriptName");
        var restoredPresets = SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(presets.ToJsonString()));
        Assert.Equal(style.FontVariant!.Value with { PostScriptName = null }, Assert.Single(restoredPresets.Presets).Style.FontVariant);
        Assert.DoesNotContain("postScriptName", Encoding.UTF8.GetString(SubtitleStylePresetMetadataStore.Serialize(restoredPresets)), StringComparison.Ordinal);
    }

    [Fact]
    public void MergeSplitTextEditsAndUndoKeepVariantInheritance()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "A");
        var second = editor.AddSubtitle(new(2), new(4), "BC");
        editor.ApplySubtitleStyle(first, Style());
        editor.ApplySubtitleStyle(second, Style() with { FontVariant = null });
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second, "");
        var line = Assert.Single(merged.Subtitles);
        Assert.Equal(Style().FontVariant, line.Style.FontVariant);
        Assert.Null(Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style).FontVariant);
        var split = ProjectEditingOperations.SplitSubtitle(merged, first, new(2), 1);
        Assert.Null(split.Subtitles[1].InlineSpans[0].Style.ApplyTo(split.Subtitles[1].Style).FontVariant);
        var before = editor.Snapshot;
        editor.ApplySubtitleInlineStyle(second, 0, 1, new() { FontVariant = Style().FontVariant });
        Assert.Equal(Style().FontVariant, editor.Snapshot.Subtitles[1].InlineSpans[0].Style.FontVariant);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        editor.ReplaceSubtitleTextRange(second, 0, 1, "XYZ");
        Assert.Equal(Style().FontVariant, editor.Snapshot.Subtitles[1].InlineSpans[0].Style.FontVariant);
    }

    [Fact]
    public async Task SystemVariantPresetCaptureAndApplyPreserveFontIdentity()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "AB");
        var preset = await SubtitleStylePresetService.CaptureAsync("Face", Style(), editor.Snapshot, directory.Path, new Presets.UnavailableFontFamilySource());
        Assert.Empty(preset.FontFamilies);
        var applied = await SubtitleStylePresetService.ApplyAsync(preset.Preset, editor.Snapshot, directory.Path, [id]);
        Assert.Equal(Style(), Assert.Single(applied.Subtitles).Style);
    }

    private static SubtitleStyle Style() => new()
    {
        FontFamily = "Example Sans", FontVariant = new() { Name = "SemiBold", PostScriptName = "ExampleSans-SemiBold", Weight = 600 }
    };
}
