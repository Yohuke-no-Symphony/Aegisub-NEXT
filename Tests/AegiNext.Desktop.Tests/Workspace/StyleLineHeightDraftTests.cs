using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleLineHeightDraftTests
{
    [Theory]
    [InlineData("0.1", 0.1)]
    [InlineData("1.8", 1.8)]
    [InlineData("10", 10)]
    public async Task LineHeightCommitsWithinTheCoreRangeAndUndoRedoRestoreDrafts(string text, double expected)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.LineHeightText = text;
        Assert.Same(original, context.Editor.Snapshot);

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Session.SelectedCue!.Style;
        Assert.Equal(expected, changed.LineHeight);
        Assert.Equal(original.Subtitles[0].Style.ShadowOffset, changed.ShadowOffset);
        Assert.Equal(original.Subtitles[0].Style.ShadowBlur, changed.ShadowBlur);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor, changed.ShadowColor);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal((decimal)original.Subtitles[0].Style.LineHeight, styles.LineHeight);
        Assert.True(context.Editor.Redo());
        Assert.Equal((decimal)expected, styles.LineHeight);
        Assert.Equal(text, styles.LineHeightText);
    }

    [Theory]
    [InlineData("7e-")]
    [InlineData("")]
    [InlineData("0.09")]
    [InlineData("10.01")]
    public async Task InvalidLineHeightBlocksSelectionAndEscapeKeepsOtherPendingStyleFields(string text)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.ShadowXText = "17";
        styles.LineHeightText = text;

        context.Session.SelectCue(document.Subtitles[1].Id);

        Assert.Equal(document.Subtitles[0].Id, context.Session.SelectedCueId);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(text, styles.LineHeightText);
        Assert.Equal("17", styles.ShadowXText);
        Assert.Equal("LineHeightInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.True(styles.RestoreNumberField("LineHeightInput"));
        Assert.Equal("17", styles.ShadowXText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(17, context.Session.SelectedCue!.Style.ShadowOffset.X);
        Assert.Equal(original.Subtitles[0].Style.LineHeight, context.Session.SelectedCue.Style.LineHeight);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task OtherTypographyAndEquivalentDecimalDraftPreserveTheOriginalDoubleLineHeight()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Session.SelectedCue!.Style.LineHeight;
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "80";
        context.Session.ViewModel.Styles.Subtitle.LineHeightText = "1.2";

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(original, context.Session.SelectedCue!.Style.LineHeight);
        Assert.Equal(80, context.Session.SelectedCue.Style.FontSize);
        context.Session.SelectCue(document.Subtitles[1].Id);
        Assert.Equal(2.2m, context.Session.ViewModel.Styles.Subtitle.LineHeight);
        Assert.Equal("2.2", context.Session.ViewModel.Styles.Subtitle.LineHeightText);
    }

    [Fact]
    public async Task ShapeEditingIgnoresInvalidSubtitleLineHeightAndLeavesSubtitlesUnchanged()
    {
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer
        {
            TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 100), End = new(4)
        };
        var document = Document();
        document = document with { Tracks = document.Tracks.Add(shapeTrack), Layers = document.Layers.Add(shape) };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.False(styles.HasCue);
        styles.LineHeightText = "invalid";
        context.Session.ViewModel.Styles.Shape.FillDraft.SetValue(new(0.25, 0.5, 0.75, 1));

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(original.Subtitles, context.Editor.Snapshot.Subtitles);
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), context.Session.SelectedLayer!.Fill);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First\nLine", End = new(4),
            Style = new()
            {
                LineHeight = 1.2000000000000002,
                ShadowOffset = new(2.0000000000000004, -3.0000000000000004),
                ShadowBlur = 2.0000000000000004,
                ShadowColor = new(1.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234)
            }
        };
        var second = new SubtitleLine
        {
            Text = "Second\nLine", Start = new(5), End = new(9), Style = new() { LineHeight = 2.2 }
        };
        return new()
        {
            Subtitles = [first, second],
            Layers =
            [
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }
            ]
        };
    }
}
