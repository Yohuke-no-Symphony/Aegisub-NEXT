using System.Text.Json.Nodes;
using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleStyleIdentityTests
{
    [Fact]
    public void MissingPersistedStyleIdentityDefaultsWithoutChangingTheProjectVersion()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "test");
        var json = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        json["subtitles"]![0]!.AsObject().Remove("styleName");

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal("Default", restored.Subtitles[0].StyleName);
        Assert.Equal(editor.Snapshot.Version, restored.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Name\nSuffix")]
    [InlineData(null)]
    public void InvalidStyleIdentityRejectsTheTransaction(string? name)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "test");
        var before = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.UpdateSubtitle(id, line => line with { StyleName = name! }));
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void AssImportRetainsTheDeclaredEventStyleName()
    {
        var source = "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,星熊uru,,0,0,0,,hello";
        var imported = AssSubtitleFormat.Parse(source);
        Assert.Equal("星熊uru", Assert.Single(imported.Lines).StyleName);
    }

    [Fact]
    public void AssImportRejectsControlCharactersInTheStyleIdentity()
    {
        var source = "[Script Info]\nScriptType: v4.00+\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Bad\tName,,0,0,0,,hello";
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(source));
    }

    [Fact]
    public void TrackStyleApplicationAndAutomaticCreationUpdateTheIdentity()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "first");
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Dialogue", new(), true);
        Assert.Equal("Dialogue", editor.Snapshot.Subtitles.Single(line => line.Id == first).StyleName);
        var second = editor.AddSubtitle(new(2), new(3), "second");
        Assert.Equal("Dialogue", editor.Snapshot.Subtitles.Single(line => line.Id == second).StyleName);
    }

    [Fact]
    public void StyleIdentityLengthIsBoundedAndDisabledTrackDefaultsKeepImportedNames()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "first");
        editor.UpdateSubtitle(id, line => line with { StyleName = new('字', 1024) });
        Assert.Throws<InvalidDataException>(() => editor.UpdateSubtitle(id, line => line with { StyleName = new('字', 1025) }));
        editor.SetSubtitleTrackStyle(ProjectTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Dialogue", new());
        editor.SetSubtitleTrackAutoApplyStyle(ProjectTrack.DEFAULT_TRACK_ID, false);
        editor.AddSubtitles([new SubtitleLine { Start = new(2), End = new(3), StyleName = "Imported" }], ProjectTrack.DEFAULT_TRACK_ID);
        Assert.Equal("Imported", editor.Snapshot.Subtitles[1].StyleName);
    }

    [Fact]
    public async Task PresetApplicationUpdatesStyleIdentity()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "test");
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "对白 Dialogue 01", new());
        var prepared = await SubtitleStylePresetService.PrepareAsync(preset, editor.Snapshot, directory.Path);
        Assert.Equal(preset.Name, prepared.StyleName);
        Assert.Equal(preset.Id, prepared.StylePresetId);
        var result = await SubtitleStylePresetService.ApplyAsync(preset, editor.Snapshot, directory.Path, [id]);
        Assert.Equal(preset.Name, result.Subtitles[0].StyleName);
        Assert.Equal(preset.Id, result.Subtitles[0].StylePresetId);
    }
}
