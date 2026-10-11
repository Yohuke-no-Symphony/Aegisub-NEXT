using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleMarginsDraftWorkflowTests
{
    [Fact]
    public async Task AsymmetricMarginDraftsStayUncommittedAndCommitWithOneUndo()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.Margins.Left.RawText = "17";
        styles.Margins.Right.RawText = "39";
        styles.Margins.Vertical.RawText = "13";

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.HasProjectDrafts);
        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Session.SelectedCue!.Style;
        Assert.Equal(new SubtitleMargins(17, 39, 13), changed.Margins);
        Assert.Equal(original.Subtitles[0].Style.LineHeight, changed.LineHeight);
        Assert.Equal(original.Subtitles[0].Style.Position, changed.Position);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        AssertDraft(styles.Margins, original.Subtitles[0].Style.Margins);
        Assert.False(context.Session.HasProjectDrafts);
        Assert.True(context.Editor.Redo());
        AssertDraft(styles.Margins, changed.Margins);
        Assert.False(context.Session.HasProjectDrafts);
    }

    [Theory]
    [InlineData("MarginLeftInput", "7e-")]
    [InlineData("MarginLeftInput", "-1")]
    [InlineData("MarginRightInput", "")]
    [InlineData("MarginRightInput", "32769")]
    [InlineData("MarginVerticalInput", "NaN")]
    [InlineData("MarginVerticalInput", "-0.1")]
    public async Task InvalidMarginBlocksTheWholeTransactionAndSelectionWithoutLosingOtherDrafts(string field, string text)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "80";
        styles.Margins.Left.RawText = "17";
        styles.Margins.Right.RawText = "39";
        styles.Margins.Vertical.RawText = "13";
        var draft = Field(styles.Margins, field);
        draft.RawText = text;

        context.Session.SelectCue(document.Subtitles[1].Id);

        Assert.Equal(document.Subtitles[0].Id, context.Session.SelectedCueId);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.HasProjectDrafts);
        Assert.Equal(text, draft.RawText);
        Assert.Equal("80", styles.FontSizeText);
        Assert.Equal("styles", context.Session.ViewModel.InvalidPanelId);
        Assert.Equal(field, context.Session.ViewModel.InvalidFieldKey);
        Assert.True(styles.RestoreNumberField(field));
        Assert.Equal("80", styles.FontSizeText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Session.TryCommitDrafts(false));
        var expected = field switch
        {
            "MarginLeftInput" => new SubtitleMargins(original.Subtitles[0].Style.Margins.Left, 39, 13),
            "MarginRightInput" => new SubtitleMargins(17, original.Subtitles[0].Style.Margins.Right, 13),
            _ => new SubtitleMargins(17, 39, original.Subtitles[0].Style.Margins.Vertical)
        };
        Assert.Equal(expected, context.Session.SelectedCue!.Style.Margins);
        Assert.Equal(80, context.Session.SelectedCue.Style.FontSize);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task RestoringOneMarginLeavesTheOtherRawDraftsEditableAndUncommitted()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.Margins.Left.RawText = "7e-";
        styles.Margins.Right.RawText = "21";
        styles.Margins.Vertical.RawText = "4e-";

        Assert.True(styles.RestoreNumberField("MarginLeftInput"));

        Assert.Equal(Projection(original.Subtitles[0].Style.Margins.Left), styles.Margins.Left.Value);
        Assert.Equal("21", styles.Margins.Right.RawText);
        Assert.Equal("4e-", styles.Margins.Vertical.RawText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("MarginVerticalInput", context.Session.ViewModel.InvalidFieldKey);
        styles.Margins.Vertical.RawText = "13";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new SubtitleMargins(original.Subtitles[0].Style.Margins.Left, 21, 13),
            context.Session.SelectedCue!.Style.Margins);
    }

    [Fact]
    public async Task UntouchedAndEquivalentMarginDraftsPreserveOriginalDoublePrecision()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "80";
        styles.Margins.Left.RawText = "17.2000000000000030";

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(original.Subtitles[0].Style.Margins, context.Session.SelectedCue!.Style.Margins);
        Assert.Equal(80, context.Session.SelectedCue.Style.FontSize);
        context.Session.SelectCue(document.Subtitles[1].Id);
        AssertDraft(styles.Margins, document.Subtitles[1].Style.Margins);
        Assert.Equal("0", styles.Margins.Left.RawText);
        Assert.Equal("23", styles.Margins.Right.RawText);
        Assert.Equal("0", styles.Margins.Vertical.RawText);
    }

    [Fact]
    public async Task ChangingMarginsKeepsAnExplicitPositionUnchanged()
    {
        var document = Document();
        var position = new SubtitlePosition
        {
            Anchor = new(0.25, 0.75), Pivot = new(0.3, 0.6), Offset = new(17.5, -24.25)
        };
        document = document with
        {
            Subtitles = document.Subtitles.SetItem(0, document.Subtitles[0] with
            {
                Style = document.Subtitles[0].Style with { Position = position }
            })
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.Margins.Left.RawText = "51";
        styles.Margins.Right.RawText = "9";
        styles.Margins.Vertical.RawText = "73";

        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(new SubtitleMargins(51, 9, 73), context.Session.SelectedCue!.Style.Margins);
        Assert.Equal(position, context.Session.SelectedCue.Style.Position);
        Assert.True(styles.Position.IsExplicit);
    }

    [Fact]
    public async Task ShapeEditingIgnoresSubtitleMarginDraftsAndLeavesSubtitlesUnchanged()
    {
        var document = Document();
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer
        {
            TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 100), End = new(4)
        };
        document = document with { Tracks = document.Tracks.Add(shapeTrack), Layers = document.Layers.Add(shape) };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.False(styles.HasCue);
        styles.Margins.Left.RawText = "invalid";
        styles.Margins.Vertical.RawText = "-1";
        styles.FillDraft.SetValue(new(0.25, 0.5, 0.75, 1));

        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Equal(original.Subtitles, context.Editor.Snapshot.Subtitles);
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), context.Session.SelectedLayer!.Fill);
    }

    private static NumericValueDraft Field(SubtitleMarginsDraft margins, string field)
    {
        return field switch
        {
            "MarginLeftInput" => margins.Left,
            "MarginRightInput" => margins.Right,
            "MarginVerticalInput" => margins.Vertical,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static void AssertDraft(SubtitleMarginsDraft draft, SubtitleMargins expected)
    {
        Assert.Equal(Projection(expected.Left), draft.Left.Value);
        Assert.Equal(Projection(expected.Right), draft.Right.Value);
        Assert.Equal(Projection(expected.Vertical), draft.Vertical.Value);
        Assert.Equal(expected, draft.CreateMargins());
        Assert.False(draft.IsDirty);
    }

    private static decimal Projection(double value)
    {
        return decimal.Parse(value.ToString("R", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First\nLine", End = new(4),
            Style = new()
            {
                Margins = new(17.200000000000003, 31.000000000000004, 5.000000000000001),
                LineHeight = 1.2000000000000002
            }
        };
        var second = new SubtitleLine
        {
            Text = "Second", Start = new(5), End = new(9), Style = new() { Margins = new(0, 23, 0) }
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
