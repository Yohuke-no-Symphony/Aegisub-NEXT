using System.Globalization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class InspectorLivePreviewUiTests
{
    [AvaloniaTheory]
    [InlineData("styles", "FontSizeInput", "72.5", 72.5)]
    [InlineData("effects", "RotationInput", "24.25", 24.25)]
    [InlineData("effects", "ScaleXInput", "1.25", 1.25)]
    [InlineData("effects", "OpacityInput", "0.65", 0.65)]
    [InlineData("effects", "BlurInput", "4.2", 4.2)]
    public async Task ValidNumericInputPresentsBeforeBlurAndCommitsWithOneUndo(string panel, string field, string text, double expected)
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        context.Window.Layouts.Activate(panel);
        var box = FocusInput(context, field);
        var before = context.Session.DocumentSnapshot;

        Enter(context, box, text);

        Assert.True(box.IsFocused);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.HasUnsavedChanges);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(expected, Read(context.Session.PreviewDocument, field));
        Assert.Same(context.Session.PreviewDocument, context.ViewModel.Preview.Scene.Document);
        Assert.Same(context.Session.PreviewDocument, context.Session.GetPreviewState().Document);
        Assert.Same(context.Session.PreviewDocument.Layers[0], context.ViewModel.Preview.Scene.SelectedLayer);
        await PresentAsync(context);
        Assert.True(box.IsFocused);

        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();

        var committed = context.Session.DocumentSnapshot;
        Assert.Equal(expected, Read(committed, field));
        Assert.Same(committed, context.Session.PreviewDocument);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task AnchorInputPreservesRawTextAndPresentsEveryValidValueBeforeEnter()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, true);
        context.Window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        var box = FocusInput(context, "AnchorXInput");
        var before = context.Session.DocumentSnapshot;
        foreach (var text in new[] { "0.20", "0.35", "0.40" })
        {
            Enter(context, box, text);
            Assert.Equal(text, box.Text);
            Assert.Equal(text, context.ViewModel.Styles.Subtitle.Position.AnchorX.RawText);
            Assert.True(box.IsFocused);
            Assert.Same(before, context.Session.DocumentSnapshot);
            Assert.Equal(double.Parse(text, CultureInfo.InvariantCulture), context.Session.PreviewDocument.Subtitles[0].Style.Position!.Anchor.X);
            await PresentAsync(context);
        }
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.4, context.Session.DocumentSnapshot.Subtitles[0].Style.Position!.Anchor.X);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task AbsolutePositionInputPresentsTheDeltaAndInvalidTextKeepsTheLastValidScene()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var box = FocusInput(context, "PositionXInput");
        var before = context.Session.DocumentSnapshot;
        var original = context.ViewModel.Effects.PositionX!.Value;
        Enter(context, box, (original + 40).ToString(CultureInfo.CurrentCulture));
        Assert.Equal(40, context.Session.PreviewDocument.Layers[0].Transform.X);
        var preview = context.Session.PreviewDocument;
        var entries = context.ViewModel.Log.Entries.Count;

        Enter(context, box, "7e-");

        Assert.True(box.IsFocused);
        Assert.Equal("7e-", box.Text);
        Assert.Equal("7e-", context.ViewModel.Effects.PositionXText);
        Assert.Same(preview, context.Session.PreviewDocument);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Equal(entries, context.ViewModel.Log.Entries.Count);
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, context.Session.PreviewDocument.Layers[0].Transform.X);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SharedRotationFieldRestoreReplacesTheImmediateVectorPreviewWithThePresentedTransform()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var box = FocusInput(context, "RotationInput");
        var before = context.Session.DocumentSnapshot;
        Enter(context, box, "24.25");
        Assert.Equal(24.25, context.Session.PreviewDocument.Layers[0].Transform.Rotation);
        await PresentAsync(context);

        context.ViewModel.Effects.RotationText = "45";
        Assert.Equal(45, context.Session.PreviewDocument.Layers[0].Transform.Rotation);
        context.ViewModel.Effects.RestoreField("RotationInput");
        Dispatcher.UIThread.RunJobs();

        Assert.True(box.IsFocused);
        Assert.Equal("0", box.Text);
        Assert.Same(before, context.Session.PreviewDocument);
        Assert.Same(before, context.ViewModel.Preview.Scene.Document);
        Assert.Same(before, context.Session.GetPreviewState().Document);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await PresentAsync(context);
    }

    [AvaloniaFact]
    public async Task KeyframeDraftPreviewsAtItsFrozenTargetAndEnterCreatesOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var id = context.Session.SelectedLayer!.Id;
        context.Session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.25));
        context.Session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(3), 0.75));
        Assert.True(context.Session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(1), new(1))));
        context.Session.Editor.Reset(context.Session.DocumentSnapshot);
        var box = FocusInput(context, "KeyframeValueInput");
        var before = context.Session.DocumentSnapshot;
        Enter(context, box, "0.4");
        Enter(context, box, "0.6");
        var previewKeys = context.Session.PreviewDocument.Layers[0].Tracks[0].Keyframes;
        Assert.Equal(new MediaTime(1), previewKeys[0].Time);
        Assert.Equal(0.6, previewKeys[0].Value.Scalar);
        Assert.Equal(0.75, previewKeys[1].Value.Scalar);
        Assert.True(box.IsFocused);
        Assert.Same(before, context.Session.DocumentSnapshot);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.6, context.Session.DocumentSnapshot.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InvalidExportDraftDoesNotBlockLocalPreviewAndFontSearchDoesNotChangeItsFont()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        var before = context.Session.DocumentSnapshot;
        context.ViewModel.Export.QualityText = "invalid";
        context.ViewModel.Styles.Subtitle.FontDraft = "Unconfirmed search query";
        var box = FocusInput(context, "FontSizeInput");
        Enter(context, box, "72.5");
        Assert.Equal(72.5, context.Session.PreviewDocument.Subtitles[0].Style.FontSize);
        Assert.Equal(before.Subtitles[0].Style.FontFamily, context.Session.PreviewDocument.Subtitles[0].Style.FontFamily);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.True(box.IsFocused);
        context.ViewModel.Styles.Subtitle.FontDraft = before.Subtitles[0].Style.FontFamily;
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Same(before, context.Session.DocumentSnapshot);
        context.ViewModel.Export.QualityText = "20";
        Assert.True(context.Session.TryCommitDrafts(false));
    }

    [AvaloniaTheory]
    [InlineData("LayerStartInput")]
    [InlineData("LayerEndInput")]
    public async Task IncompleteTimingTextKeepsFocusAndDoesNotThrowFromThePreviewQueue(string field)
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context);
        UiTestActions.ExpandEffectsCategory(context.Window, "ClipCategory");
        var box = UiTestActions.Find<TextBox>(context.Window, field);
        box.BringIntoView();
        context.Window.UpdateLayout();
        var before = context.Session.DocumentSnapshot;
        var entries = context.ViewModel.Log.Entries.Count;
        Enter(context, box, "00:00:");
        Assert.True(box.IsFocused);
        Assert.Equal("00:00:", box.Text);
        Assert.Same(before, context.Session.PreviewDocument);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Equal(entries, context.ViewModel.Log.Entries.Count);
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.Session.TryCommitDrafts(false));
    }

    private static async Task PrepareAsync(MainWindowTestContext context, bool explicitPosition = false)
    {
        await context.OpenMediaAsync();
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        context.Session.Editor.SetSubtitleTiming(cue.Id, cue.Start, cue.Start + new MediaTime(5), TimelineEditMode.CROP);
        context.Session.Editor.UpdateSubtitle(cue.Id, line => line with
        {
            Text = "Subtitle ABC 中文 123",
            Style = line.Style with { FontFamily = "sans-serif", Position = explicitPosition ? new() : null }
        });
        context.Session.SelectCue(cue.Id);
        await context.Window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_EFFECTS);
        context.Session.Editor.Reset(context.Session.DocumentSnapshot);
        await context.Session.SeekFromUserAsync(MediaTime.Zero);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox FocusInput(MainWindowTestContext context, string name)
    {
        if (name is "BlurInput" or "OpacityInput")
        {
            UiTestActions.ExpandEffectsCategory(context.Window, "CompositeCategory");
        }
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, name);
        input.BringIntoView();
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        return box;
    }

    private static void Enter(MainWindowTestContext context, TextBox box, string text)
    {
        Assert.True(box.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static double Read(ProjectDocument document, string field) => field switch
    {
        "FontSizeInput" => document.Subtitles[0].Style.FontSize,
        "RotationInput" => document.Layers[0].Transform.Rotation,
        "ScaleXInput" => document.Layers[0].Transform.ScaleX,
        "OpacityInput" => document.Layers[0].Opacity,
        "BlurInput" => document.Layers[0].Blur,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static async Task PresentAsync(MainWindowTestContext context)
    {
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = context.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            await canvas.PreviewCompletion;
            Dispatcher.UIThread.RunJobs();
            if (canvas.PresentedPreviewSequence == canvas.PreviewSequence)
            {
                return;
            }
            await Task.Yield();
        } while (DateTime.UtcNow < deadline);
        Assert.Fail("The latest inspector draft was not presented in the preview.");
    }
}
