using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleFormatImportTests
{
    private static readonly string[] firstOverlap = ["first", "middle"];
    private static readonly string[] secondOverlap = ["middle", "last"];
    [Fact]
    public void OverlapsAllocateIndependentTracksWithoutApplyingDefaultsAndUndoAtomically()
    {
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(editor.Snapshot.Tracks[0].Id, Guid.NewGuid(), "Existing", new() { FontSize = 400 });
        var before = editor.Snapshot;
        SubtitleLine[] lines = [
            new() { Start = new(2), End = new(4), Text = "first", Style = new() { FontSize = 22 } },
            new() { Start = new(0), End = new(3), Text = "overlap", Style = new() { Bold = true } },
            new() { Start = new(3), End = new(5), Text = "adjacent" }
        ];
        editor.ImportSubtitleLines(lines, "Imported");
        var imported = editor.Snapshot;
        Assert.Equal(3, imported.Tracks.Length);
        Assert.All(imported.Tracks.Take(imported.Tracks.Length - 1), track => Assert.False(track.AutoApplyStyle));
        Assert.Equal(lines.Select(line => line.Id), imported.Subtitles.Select(line => line.Id));
        Assert.Equal(lines.Select(line => line.Style), imported.Subtitles.Select(line => line.Style));
        Assert.NotEqual(new ProjectClipIndex(imported).GetSubtitleTrackId(imported.Subtitles[0].Id), new ProjectClipIndex(imported).GetSubtitleTrackId(imported.Subtitles[1].Id));
        Assert.Equal(new ProjectClipIndex(imported).GetSubtitleTrackId(imported.Subtitles[1].Id), new ProjectClipIndex(imported).GetSubtitleTrackId(imported.Subtitles[2].Id));
        Assert.Equal(3, imported.Layers.Length);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(imported, editor.Snapshot);
    }

    [Fact]
    public void InvalidBatchPreservesProjectHistoryAndNoPartialTracks()
    {
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        var line = new SubtitleLine { Text = "valid" };
        Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines([line, line with { Id = Guid.NewGuid(), End = line.Start }], "Import"));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines([null!], "Null"));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        editor.ImportSubtitleLines([], "Empty");
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void LargeOverlappingBatchReusesTracksAtAdjacentBoundary()
    {
        var lines = Enumerable.Range(0, 8000).Select(index => new SubtitleLine
        {
            Start = new(index / 4000), End = new(index / 4000 + 1), Text = index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).ToArray();
        var imported = ProjectEditingOperations.ImportSubtitleLines(new(), lines, "Large");
        Assert.Equal(4001, imported.Tracks.Length);
        Assert.Equal(8000, imported.Subtitles.Length);
        Assert.Equal(lines.Select(line => line.Id), imported.Subtitles.Select(line => line.Id));
        Assert.All(imported.Layers.GroupBy(clip => clip.TrackId), track => Assert.Equal(2, track.Count()));
    }

    [Fact]
    public void OversizedBatchRejectsBeforeChangingHistory()
    {
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines(Enumerable.Repeat(new SubtitleLine(), 100001), "Too large"));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void IntervalChainsPreserveSourceStackingAcrossLaneReuseAndAssRoundTrips()
    {
        SubtitleLine[] lines =
        [
            new() { Start = new(0), End = new(2), Text = "first" },
            new() { Start = new(1), End = new(3), Text = "middle" },
            new() { Start = new(2), End = new(4), Text = "last" }
        ];
        var imported = ProjectEditingOperations.ImportSubtitleLines(new(), lines, "Imported");
        var first = AegiNext.Core.Editing.SceneEvaluator.Evaluate(imported, new(3, 2));
        var second = AegiNext.Core.Editing.SceneEvaluator.Evaluate(imported, new(5, 2));

        Assert.Equal(firstOverlap, first.Select(clip => clip.Subtitle!.Text));
        Assert.Equal(secondOverlap, second.Select(clip => clip.Subtitle!.Text));
        Assert.Equal(4, imported.Tracks.Length);
        var ass = AssSubtitleFormat.Write(imported);
        var reopened = ProjectEditingOperations.ImportSubtitleLines(new(), AssSubtitleFormat.Parse(ass.Text), "ASS");
        Assert.Equal(first.Select(clip => clip.Subtitle!.Text),
            AegiNext.Core.Editing.SceneEvaluator.Evaluate(reopened, new(3, 2)).Select(clip => clip.Subtitle!.Text));
        Assert.Equal(second.Select(clip => clip.Subtitle!.Text),
            AegiNext.Core.Editing.SceneEvaluator.Evaluate(reopened, new(5, 2)).Select(clip => clip.Subtitle!.Text));
    }
}
