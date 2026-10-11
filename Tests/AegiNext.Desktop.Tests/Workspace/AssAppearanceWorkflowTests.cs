using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssAppearanceWorkflowTests
{
    [Theory]
    [InlineData(0, 2, SubtitleWrapMode.NO_WRAP)]
    [InlineData(2, 1, SubtitleWrapMode.NATURAL)]
    public async Task ImportedAppearanceRemainsEditableThroughStyleAndSelectionDraftsWithIndependentUndo(int border, int wrap, SubtitleWrapMode mode)
    {
        await using var context = new WorkspaceSessionTestContext(new() { Width = 640, Height = 360 });
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "appearance.ass");
        await File.WriteAllTextAsync(path, Source(border, wrap));
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);

        Assert.Null(context.Session.LastError);
        Assert.Equal(border == 0 ? 1 : 2, context.Dialogs.ConversionDiagnostics.Count);
        Assert.Single(context.Dialogs.ConversionDiagnostics, diagnostic =>
            diagnostic.StartsWith("Ass.BlurAppearance:", StringComparison.Ordinal));
        if (border > 0)
        {
            Assert.Single(context.Dialogs.ConversionDiagnostics, diagnostic =>
                diagnostic.StartsWith("Ass.ShadowComposition:", StringComparison.Ordinal));
        }
        var imported = context.Editor.Snapshot;
        var line = Assert.Single(imported.Subtitles);
        Assert.Equal(line.Id, context.Session.SelectedCueId);
        Assert.Equal(-1.25, line.Style.LetterSpacing);
        Assert.Equal(mode, line.Style.WrapMode);
        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.Equal(-1.25m, styles.LetterSpacing);
        Assert.Equal((int)mode, styles.WrapMode);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 2));
        var sigma = 4 * 2 / Math.Sqrt(Math.Log(256));
        Assert.Equal(3.5, double.Parse(details.StyleDraft.LetterSpacingText, CultureInfo.InvariantCulture));
        Assert.Equal(border == 0 ? sigma : 0, double.Parse(details.StyleDraft.FillBlurText, CultureInfo.InvariantCulture), 10);
        Assert.Equal(border > 0 ? sigma : 0, double.Parse(details.StyleDraft.StrokeBlurText, CultureInfo.InvariantCulture), 10);
        Assert.Equal(sigma, double.Parse(details.StyleDraft.ShadowBlurText, CultureInfo.InvariantCulture), 10);
        if (border == 0)
        {
            details.StyleDraft.FillBlurText = "6";
        }
        else
        {
            details.StyleDraft.StrokeBlurText = "6";
        }
        var preview = StyleAt(context.Session.PreviewDocument.Subtitles[0], 0);
        Assert.Equal(6, border == 0 ? preview.FillBlur : preview.StrokeBlur);
        Assert.Equal(sigma, preview.ShadowBlur, 10);
        Assert.Same(imported, context.Editor.Snapshot);
        Assert.True(details.CompleteInput(border == 0 ? nameof(SubtitleDetailsStyleDraft.FillBlurText) :
            nameof(SubtitleDetailsStyleDraft.StrokeBlurText), false));
        var edited = StyleAt(context.Session.SelectedCue!, 0);
        Assert.Equal(6, border == 0 ? edited.FillBlur : edited.StrokeBlur);
        var untouched = StyleAt(context.Session.SelectedCue!, 2);
        Assert.Equal(sigma, border == 0 ? untouched.FillBlur : untouched.StrokeBlur, 10);
        Assert.True(context.Editor.Undo());
        Assert.Same(imported, context.Editor.Snapshot);

        styles.LetterSpacingText = "-2";
        Assert.Equal("-2", styles.LetterSpacingText);
        Assert.Same(imported, context.Editor.Snapshot);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(-2, context.Session.SelectedCue!.Style.LetterSpacing);
        Assert.Equal(3.5, StyleAt(context.Session.SelectedCue, 0).LetterSpacing);
        Assert.True(context.Editor.Undo());
        Assert.Same(imported, context.Editor.Snapshot);
        styles.CommitWrapMode((int)SubtitleWrapMode.GRAPHEME);
        Assert.Equal(SubtitleWrapMode.GRAPHEME, context.Session.SelectedCue!.Style.WrapMode);
        Assert.True(context.Editor.Undo());
        Assert.Same(imported, context.Editor.Snapshot);
        Assert.Equal((int)mode, styles.WrapMode);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset) => line.InlineSpans.FirstOrDefault(span =>
        span.Utf16Start <= offset && span.Utf16Start + span.Utf16Length > offset)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static string Source(int border, int wrap) =>
        "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nLayoutResX: 640\nLayoutResY: 360\nWrapStyle: 1\n" +
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Spacing, Outline, Shadow, Alignment, MarginL, MarginR, MarginV\n" +
        $"Style: Default,sans-serif,28,&H00FFFFFF,&H00808080,&H00000000,&H00000000,-1.25,{border},2,5,20,20,20\n" +
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        $"Dialogue: 0,0:00:00.00,0:00:04.00,Default,,0,0,0,,{{\\fsp3.5\\blur4\\q{wrap}}}AB 中文\n";
}
