using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitlePositionBatchResetTests
{
    [Fact]
    public void UnknownTargetRejectsTheWholeResetWithoutChangingTheDocumentOrHistory()
    {
        var document = Document();
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() => editor.ResetSubtitlePositions([document.Subtitles[0].Id, Guid.NewGuid()]));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void EmptyResetPreservesTheSnapshotAndRedoHistory()
    {
        var document = Document();
        var editor = new ProjectEditor(document);
        editor.ResetSubtitlePosition(document.Subtitles[0].Id);
        Assert.True(editor.Undo());
        Assert.True(editor.CanRedo);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ResetSubtitlePositions([]);

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RepeatedTargetsAreResetOnceInASingleTransaction()
    {
        var document = Document();
        var editor = new ProjectEditor(document);
        var first = document.Subtitles[0].Id;
        var second = document.Subtitles[1].Id;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ResetSubtitlePositions([first, second, first]);

        var changed = editor.Snapshot;
        Assert.All(changed.Subtitles, line => Assert.Null(line.Style.Position));
        Assert.All(changed.Layers, layer => Assert.Equal(default, layer.Transform.Position));
        Assert.Equal(1, changes);
        editor.ResetSubtitlePositions([first, second]);
        Assert.Same(changed, editor.Snapshot);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First", End = new(4), Style = new() { Position = new() { Offset = new(10, 20) } }
        };
        var second = new SubtitleLine
        {
            Text = "Second", Start = new(5), End = new(9), Style = new() { Position = new() { Offset = new(30, 40) } }
        };
        return new()
        {
            Subtitles = [first, second],
            Layers = [.. new[] { first, second }.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
                Transform = new(15, 25)
            })]
        };
    }
}
