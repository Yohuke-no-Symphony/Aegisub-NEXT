using AegiNext.Core.Projects;
using AegiNext.Core.Presets;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleScalingWorkspaceTests
{
    [Fact]
    public async Task LegacyHighlightPresetEntryUsesTheSelectedSubtitlesReferenceUnits()
    {
        var line = new SubtitleLine { Text = "AB", End = new(2), Style = new()
        {
            FontFamily = "sans-serif", Scaling = new() { ReferenceWidth = 960, ReferenceHeight = 540 }
        } };
        var document = new ProjectDocument { Width = 960, Height = 540, Subtitles = [line],
            Layers = [new() { Id = line.Id, SubtitleId = line.Id, End = line.End }] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Highlight", new()
        {
            StrokeWidth = 4, ShadowOffset = new(-6, 8), ShadowBlur = 2
        });
        await context.Session.Styles.UpsertAsync(preset);
        context.Session.SelectCue(line.Id);

        context.Session.CreateKaraoke(preset.Id);

        var highlight = context.Session.SelectedCue!.KaraokeStyle!;
        Assert.Equal(2, highlight.StrokeWidth);
        Assert.Equal(new ScenePoint(-3, 4), highlight.ShadowOffset);
        Assert.Equal(1, highlight.ShadowBlur);
        Assert.Equal(line.Style, context.Session.SelectedCue.Style);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ReferenceSettingsCommitOnceAndUndoRedoRestoreDraftsWithoutBakingAppearance()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.Scaling.ReferenceWidth.RawText = "1280";
        styles.Scaling.ReferenceHeight.RawText = "720";
        styles.Scaling.MatchWidthOrHeight = 0.75;
        Assert.Same(document, context.Editor.Snapshot);

        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = context.Session.SelectedCue!;
        var expected = new SubtitleScalingSettings { ReferenceWidth = 1280, ReferenceHeight = 720, MatchWidthOrHeight = 0.75 };
        Assert.Equal(expected, changed.Style.Scaling);
        Assert.Equal(document.Subtitles[0].Style.FontSize, changed.Style.FontSize);
        Assert.Equal(document.Subtitles[0].Style.Position, changed.Style.Position);
        Assert.Empty(changed.InlineSpans);
        Assert.Equal(document.Layers[0].Tracks, context.Session.SelectedLayer!.Tracks);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("1920", styles.Scaling.ReferenceWidth.RawText);
        Assert.Equal(0.5, styles.Scaling.MatchWidthOrHeight);
        Assert.True(context.Editor.Redo());
        Assert.Equal(expected, styles.Scaling.CreateSettings());
    }

    [Theory]
    [InlineData("")]
    [InlineData("7e-")]
    [InlineData("0")]
    [InlineData("32769")]
    [InlineData("1920.5")]
    public async Task InvalidReferenceBlocksSelectionAndFieldRestoreKeepsOtherDrafts(string text)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);
        var styles = context.Session.ViewModel.Styles.Subtitle;
        styles.Scaling.ReferenceWidth.RawText = text;
        styles.Scaling.ReferenceHeight.RawText = "720";
        styles.ShadowXText = "17";

        context.Session.SelectCue(document.Subtitles[1].Id);

        Assert.Equal(document.Subtitles[0].Id, context.Session.SelectedCueId);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("ReferenceWidthInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal(text, styles.Scaling.ReferenceWidth.RawText);
        Assert.True(styles.RestoreNumberField("ReferenceWidthInput"));
        Assert.Equal("1920", styles.Scaling.ReferenceWidth.RawText);
        Assert.Equal("720", styles.Scaling.ReferenceHeight.RawText);
        Assert.Equal("17", styles.ShadowXText);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(720, context.Session.SelectedCue!.Style.Scaling.ReferenceHeight);
        Assert.Equal(17, context.Session.SelectedCue.Style.ShadowOffset.X);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CanvasChangesKeepReferenceSettingsAndSelectionLoadsTheNextStyle()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(document.Subtitles[0].Id);

        context.Editor.Apply("Change canvas", value => value with { Width = 1080, Height = 1920 });

        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.Equal(document.Subtitles[0].Style.Scaling, styles.Scaling.CreateSettings());
        Assert.Equal(document.Subtitles[0].Style.FontSize, context.Session.SelectedCue!.Style.FontSize);
        context.Session.SelectCue(document.Subtitles[1].Id);
        Assert.Equal(document.Subtitles[1].Style.Scaling, styles.Scaling.CreateSettings());
        Assert.Equal("720", styles.Scaling.ReferenceWidth.RawText);
        Assert.Equal("1280", styles.Scaling.ReferenceHeight.RawText);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "AB", End = new(2), Style = new()
            {
                FontFamily = "sans-serif", FontSize = 40,
                Position = new() { Offset = new(10, -20) }
            },
            InlineSpans = [new(1, 1, new() { FontSize = 60 })]
        };
        var second = new SubtitleLine { Text = "CD", Start = new(2), End = new(4), Style = new()
        {
            FontFamily = "sans-serif", Scaling = new() { ReferenceWidth = 720, ReferenceHeight = 1280, MatchWidthOrHeight = 1 }
        } };
        return new() { Subtitles = [first, second], Layers =
        [
            new() { Id = first.Id, SubtitleId = first.Id, End = first.End,
                Tracks = [new(AnimationProperty.STROKE_WIDTH, [new(new(0), 2), new(new(2), 4)])] },
            new() { Id = second.Id, SubtitleId = second.Id, Start = second.Start, End = second.End }
        ] };
    }
}
