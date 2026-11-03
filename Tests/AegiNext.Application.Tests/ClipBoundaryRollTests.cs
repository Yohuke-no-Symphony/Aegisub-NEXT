using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipBoundaryRollTests
{
    [Theory]
    [InlineData(11, 2)]
    [InlineData(13, 2)]
    public void RollMovesTheSharedBoundaryOfBothClipsInOneUndoableStep(long numerator, int denominator)
    {
        var (editor, left, right) = Create();
        var before = editor.Snapshot;
        var boundary = new MediaTime(numerator, denominator);

        editor.RollClipBoundary(left.Id, right.Id, boundary);

        var after = editor.Snapshot;
        Assert.Equal((left.Start, boundary), (after.Subtitles[0].Start, after.Subtitles[0].End));
        Assert.Equal((boundary, right.End), (after.Subtitles[1].Start, after.Subtitles[1].End));
        Assert.Equal(after.Subtitles[0].End, after.Layers[0].End);
        Assert.Equal(after.Subtitles[1].Start, after.Layers[1].Start);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(4)]
    [InlineData(8)]
    public void RollRejectsABoundaryThatWouldLeaveEitherClipEmpty(int seconds)
    {
        var (editor, left, right) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.RollClipBoundary(left.Id, right.Id, new(seconds)));
    }

    [Fact]
    public void RollRequiresClipsThatShareTheBoundary()
    {
        var (editor, left, right) = Create();
        editor.MoveSubtitleClip(right.Id, editor.Snapshot.Layers[1].TrackId, new(7), new(8), AegiNext.Core.Editing.TimelineEditMode.CROP, true);
        Assert.Throws<ArgumentException>(() => editor.RollClipBoundary(left.Id, right.Id, new(11, 2)));
    }

    private static (ProjectEditor Editor, SubtitleLine Left, SubtitleLine Right) Create()
    {
        var left = new SubtitleLine { Text = "a", Start = new(5), End = new(6) };
        var right = new SubtitleLine { Text = "b", Start = new(6), End = new(7) };
        var document = new ProjectDocument
        {
            Subtitles = [left, right],
            Layers =
            [
                new() { Id = left.Id, Kind = LayerKind.SUBTITLE, SubtitleId = left.Id, Start = left.Start, End = left.End },
                new() { Id = right.Id, Kind = LayerKind.SUBTITLE, SubtitleId = right.Id, Start = right.Start, End = right.End }
            ]
        };
        return (new ProjectEditor(document), left, right);
    }
}
