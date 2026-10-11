using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleShadowDraftTests
{
    [Fact]
    public async Task ShadowFieldsCommitTogetherAndUndoRedoRestoreTheExactStyleAndDrafts()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.Equal(original.Subtitles[0].Style.ShadowColor, styles.ShadowDraft.Value);
        styles.ShadowXText = "17";
        styles.ShadowYText = "-9";
        styles.ShadowBlurText = "8";
        styles.ShadowDraft.HexText = "#00FF0080";
        Assert.Same(original, context.Editor.Snapshot);

        Assert.True(context.Session.TryCommitDrafts());

        var changed = context.Session.SelectedCue!.Style;
        Assert.Equal(new ScenePoint(17, -9), changed.ShadowOffset);
        Assert.Equal(8, changed.ShadowBlur);
        Assert.Equal(new SceneColor(0, 1, 0, 128d / 255), changed.ShadowColor);
        Assert.Equal(original.Subtitles[0].Style.Fill, changed.Fill);
        Assert.Equal(original.Subtitles[0].Style.Stroke, changed.Stroke);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal((decimal)original.Subtitles[0].Style.ShadowOffset.X, styles.ShadowX);
        Assert.Equal((decimal)original.Subtitles[0].Style.ShadowOffset.Y, styles.ShadowY);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor, styles.ShadowDraft.Value);
        Assert.True(context.Editor.Redo());
        Assert.Equal("17", styles.ShadowXText);
        Assert.Equal("-9", styles.ShadowYText);
        Assert.Equal("8", styles.ShadowBlurText);
        Assert.Equal(changed.ShadowColor, styles.ShadowDraft.Value);
    }

    [Theory]
    [InlineData("ShadowXInput", "7e-")]
    [InlineData("ShadowXInput", "-1000000001")]
    [InlineData("ShadowYInput", "1000000001")]
    [InlineData("ShadowBlurInput", "-1")]
    [InlineData("ShadowBlurInput", "513")]
    public async Task InvalidShadowNumbersBlockTheWholeTransactionAndPreserveRawText(string field, string text)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "80";
        switch (field)
        {
            case "ShadowXInput":
                styles.ShadowXText = text;
                break;
            case "ShadowYInput":
                styles.ShadowYText = text;
                break;
            case "ShadowBlurInput":
                styles.ShadowBlurText = text;
                break;
        }

        Assert.False(context.Session.TryCommitDrafts(false));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("styles", context.Session.ViewModel.InvalidPanelId);
        Assert.Equal(field, context.Session.ViewModel.InvalidFieldKey);
        Assert.True(styles.RestoreShadowField(field));
        Assert.Equal("80", styles.FontSizeText);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(80, context.Session.SelectedCue!.Style.FontSize);
        Assert.Equal(original.Subtitles[0].Style.ShadowOffset, context.Session.SelectedCue.Style.ShadowOffset);
        Assert.Equal(original.Subtitles[0].Style.ShadowBlur, context.Session.SelectedCue.Style.ShadowBlur);
    }

    [Fact]
    public async Task InvalidShadowColorBlocksSelectionAndPreservesOtherPendingFields()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.ShadowYText = "-12";
        styles.ShadowDraft.HexText = "#broken";

        context.Session.SelectCue(document.Subtitles[1].Id);

        Assert.Equal(document.Subtitles[0].Id, context.Session.SelectedCueId);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("#broken", styles.ShadowDraft.HexText);
        Assert.Equal("-12", styles.ShadowYText);
        Assert.Equal("ShadowPicker", context.Session.ViewModel.InvalidFieldKey);
        styles.ShadowDraft.Restore("HexText");
        context.Session.SelectCue(document.Subtitles[1].Id);
        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.Equal(document.Subtitles[1].Style.ShadowColor, styles.ShadowDraft.Value);
        Assert.Equal((decimal)document.Subtitles[1].Style.ShadowOffset.X, styles.ShadowX);
        Assert.Equal((decimal)document.Subtitles[1].Style.ShadowOffset.Y, styles.ShadowY);
        Assert.Equal(-12, context.Editor.Snapshot.Subtitles[0].Style.ShadowOffset.Y);
    }

    [Fact]
    public async Task RestoringOneAxisKeepsTheOtherAxisAndHdrColorPendingWithoutCommitting()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.ShadowXText = "7e-";
        styles.ShadowYText = "17";
        styles.ShadowDraft.Red.RawText = "2";

        Assert.True(styles.RestoreShadowField("ShadowXInput"));

        Assert.Equal("17", styles.ShadowYText);
        Assert.Equal("2", styles.ShadowDraft.Red.RawText);
        Assert.True(styles.ShadowDraft.IsDirty);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.TryCommitDrafts(false));
        var shadow = context.Session.SelectedCue!.Style;
        Assert.Equal(original.Subtitles[0].Style.ShadowOffset.X, shadow.ShadowOffset.X);
        Assert.Equal(17, shadow.ShadowOffset.Y);
        Assert.Equal(2, shadow.ShadowColor.Red);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor.Green, shadow.ShadowColor.Green);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor.Blue, shadow.ShadowColor.Blue);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor.Alpha, shadow.ShadowColor.Alpha);
        Assert.False(styles.RestoreShadowField("FontSizeInput"));
    }

    [Fact]
    public async Task EditingTypographyKeepsUneditedShadowPrecisionAndHdrValues()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "80";

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Session.SelectedCue!.Style;
        Assert.Equal(original.Subtitles[0].Style.ShadowOffset, changed.ShadowOffset);
        Assert.Equal(original.Subtitles[0].Style.ShadowBlur, changed.ShadowBlur);
        Assert.Equal(original.Subtitles[0].Style.ShadowColor, changed.ShadowColor);
    }

    [Fact]
    public async Task ShapeEditingIgnoresSubtitleShadowDraftsAndDoesNotChangeOtherSubtitles()
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
        styles.ShadowXText = "invalid";
        styles.ShadowDraft.HexText = "#broken";
        context.Session.ViewModel.Styles.Shape.FillDraft.SetValue(new(0.25, 0.5, 0.75, 1));

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(original.Subtitles, context.Editor.Snapshot.Subtitles);
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), context.Session.SelectedLayer!.Fill);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First", End = new(4),
            Style = new()
            {
                ShadowOffset = new(2.0000000000000004, -3.0000000000000004),
                ShadowBlur = 2.0000000000000004,
                ShadowColor = new(1.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234)
            }
        };
        var second = new SubtitleLine
        {
            Text = "Second", Start = new(5), End = new(9),
            Style = new() { ShadowOffset = new(-8, 9), ShadowBlur = 3, ShadowColor = new(0.1, 0.2, 0.3, 0.4) }
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
