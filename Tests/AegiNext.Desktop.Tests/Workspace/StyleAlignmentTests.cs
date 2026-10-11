using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleAlignmentTests
{
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
    public async Task SelectingAlignmentUsesTheNativeValueAndClearsLegacyOverrideInOneUndo(TextAlignment alignment)
    {
        var document = Document(SubtitleTextAlignment.RIGHT);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;

        context.Session.ViewModel.Styles.Subtitle.CommitAlignment((int)alignment);

        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(original.Subtitles[0] with { Style = original.Subtitles[0].Style with
        {
            Alignment = alignment, TextAlign = null
        } }, changed);
        Assert.Equal(original.Layers, context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Equal((int)alignment, context.Session.ViewModel.Styles.Subtitle.Alignment);
        Assert.Null(context.Session.SelectedCue!.Style.TextAlign);
    }

    [Fact]
    public async Task OpeningAndEditingOtherStyleFieldsKeepsLegacyAlignment()
    {
        var document = Document(SubtitleTextAlignment.LEFT);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        Assert.Equal((int)TextAlignment.BOTTOM_CENTER, context.Session.ViewModel.Styles.Subtitle.Alignment);
        Assert.Same(original, context.Editor.Snapshot);

        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "80";
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal(SubtitleTextAlignment.LEFT, context.Session.SelectedCue!.Style.TextAlign);
        Assert.Equal(80, context.Session.SelectedCue.Style.FontSize);
    }

    [Fact]
    public async Task AlignmentAndValidDetailsAndStyleDraftsShareOneTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "Z");
        details.StyleDraft.FontSizeText = "88";
        context.Session.ViewModel.Styles.Subtitle.FontSizeText = "76";

        context.Session.ViewModel.Styles.Subtitle.CommitAlignment((int)TextAlignment.TOP_RIGHT);

        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("AZCD\nEF", changed.Text);
        Assert.Equal(88, changed.InlineSpans[0].Style.FontSize);
        Assert.Equal(76, changed.Style.FontSize);
        Assert.Equal(TextAlignment.TOP_RIGHT, changed.Style.Alignment);
        Assert.Null(changed.Style.TextAlign);
        Assert.Equal(original.Subtitles[0].Karaoke, changed.Karaoke);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDraftPreventsPartialAlignmentCommitAndCanBeCorrected(bool duration)
    {
        await using var context = new WorkspaceSessionTestContext(Document(SubtitleTextAlignment.LEFT));
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        if (duration)
        {
            Assert.True(details.SelectClip(original.Subtitles[0].Karaoke[0].Id));
        }
        details.EditText(1, 1, "Z");
        if (duration)
        {
            details.EditDuration("invalid");
        }
        else
        {
            context.Session.ViewModel.Styles.Subtitle.FontSizeText = "invalid";
        }

        context.Session.ViewModel.Styles.Subtitle.CommitAlignment((int)TextAlignment.TOP_LEFT);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(SubtitleTextAlignment.LEFT, context.Session.SelectedCue!.Style.TextAlign);
        Assert.Equal("AZCD\nEF", details.Line!.Text);
        if (duration)
        {
            Assert.Equal("invalid", details.DurationText);
            details.EditDuration("1");
        }
        else
        {
            Assert.Equal("invalid", context.Session.ViewModel.Styles.Subtitle.FontSizeText);
            context.Session.ViewModel.Styles.Subtitle.FontSizeText = "80";
        }
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal(TextAlignment.TOP_LEFT, context.Session.SelectedCue.Style.Alignment);
        Assert.Null(context.Session.SelectedCue.Style.TextAlign);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ClickingTheCurrentAlignmentKeepsSnapshotAndRedoWhenNoLegacyOverrideExists()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        context.Session.SelectCue(context.Editor.Snapshot.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;

        styles.CommitAlignment((int)TextAlignment.BOTTOM_CENTER);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        styles.CommitAlignment((int)TextAlignment.TOP_LEFT);
        var changed = context.Editor.Snapshot;
        styles.CommitAlignment((int)TextAlignment.TOP_LEFT);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        styles.CommitAlignment((int)TextAlignment.BOTTOM_CENTER);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Editor.CanRedo);
    }

    private static ProjectDocument Document(SubtitleTextAlignment? legacy = null)
    {
        var first = new SubtitleLine
        {
            Text = "ABCD\nEF", End = new(4),
            Style = new()
            {
                Alignment = TextAlignment.BOTTOM_CENTER, TextAlign = legacy,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(7, -12) }
            },
            InlineSpans = [new(0, 1, new() { Bold = true })],
            Karaoke = [new(0, 4, MediaTime.Zero, new(1), SceneColor.White), new(5, 2, new(1), new(2), SceneColor.White)]
        };
        return new()
        {
            Subtitles = [first],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End }]
        };
    }
}
