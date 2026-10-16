using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleStyleRichTextTests
{
    [Theory]
    [InlineData("FontSize", false)]
    [InlineData("FontSize", true)]
    [InlineData("Fill", false)]
    [InlineData("Fill", true)]
    [InlineData("ShadowOffset", false)]
    [InlineData("ShadowOffset", true)]
    public async Task StyleEditsClearInlineFormattingInPreviewAndCommitWithOneUndo(string field, bool karaoke)
    {
        var document = Document(karaoke);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        var styles = context.Session.ViewModel.Styles.Subtitle;
        var expectedStyle = line.Style;
        switch (field)
        {
            case "FontSize":
                styles.FontSizeText = "80";
                expectedStyle = expectedStyle with { FontSize = 80 };
                break;
            case "Fill":
                styles.FillDraft.SetValue(new(0, 1, 0));
                expectedStyle = expectedStyle with { Fill = new(0, 1, 0) };
                break;
            case "ShadowOffset":
                styles.ShadowXText = "17";
                expectedStyle = expectedStyle with { ShadowOffset = new(17, expectedStyle.ShadowOffset.Y) };
                break;
        }

        var preview = styles.Prepare(original, true);

        Assert.Equal(line with { Style = expectedStyle, InlineSpans = [] }, preview.Subtitles[0]);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.TryCommitDrafts(false));
        var changed = context.Editor.Snapshot;
        Assert.Equal(preview.Subtitles[0], changed.Subtitles[0]);
        Assert.Equal(karaoke ? SubtitleContentKind.KARAOKE : SubtitleContentKind.PLAIN, changed.Subtitles[0].ContentKind);
        Assert.Equal(original.Subtitles[1], changed.Subtitles[1]);
        Assert.Equal(original.Layers, changed.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task AnimatedStyleEditClearsInlineFormattingAndUndoRestoresAnimationAndFormatting()
    {
        var document = Document();
        document = document with
        {
            Layers = document.Layers.SetItem(0, document.Layers[0] with
            {
                Tracks = [new(AnimationProperty.STROKE_WIDTH, [new(new(0), 2), new(new(2), 4)])]
            })
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        context.Session.ViewModel.Styles.Subtitle.StrokeWidthText = "8";

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        Assert.Equal(original.Subtitles[0] with { InlineSpans = [] }, changed.Subtitles[0]);
        Assert.Equal(8, changed.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.Equal(original.Subtitles[1], changed.Subtitles[1]);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task InvalidStyleDraftKeepsInlineFormattingUntilTheWholeEditIsValid()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "invalid";
        styles.ShadowXText = "17";

        Assert.False(context.Session.TryCommitDrafts(false));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("invalid", styles.FontSizeText);
        Assert.Equal("17", styles.ShadowXText);
        styles.FontSizeText = "80";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Empty(context.Session.SelectedCue!.InlineSpans);
        Assert.Equal(80, context.Session.SelectedCue.Style.FontSize);
        Assert.Equal(17, context.Session.SelectedCue.Style.ShadowOffset.X);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task UnchangedStyleDraftKeepsInlineFormattingAndRedoHistory()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.FontSizeText = "80";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.True(context.Editor.Undo());

        styles.FontSizeText = "40.0";
        Assert.True(context.Session.TryCommitDrafts(false));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
    }

    private static ProjectDocument Document(bool karaoke = false)
    {
        var first = new SubtitleLine
        {
            Text = "ABCD", End = new(4), StyleName = "Imported",
            Style = new() { FontFamily = "sans-serif", FontSize = 40 },
            InlineSpans =
            [
                new(0, 2, new() { FontSize = 90, Bold = true, Fill = new(1, 0, 0) }),
                new(2, 2, new() { Italic = true, ShadowOffset = new(5, 6) })
            ],
            Karaoke = karaoke ? [new(0, 4, MediaTime.Zero, new(2), SceneColor.White)] : []
        };
        var second = new SubtitleLine
        {
            Text = "Other", Start = new(5), End = new(9),
            InlineSpans = [new(0, 5, new() { Bold = true })]
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
