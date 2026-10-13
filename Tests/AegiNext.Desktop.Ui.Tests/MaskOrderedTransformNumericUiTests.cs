using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskOrderedTransformNumericUiTests
{
    [AvaloniaTheory]
    [InlineData(false, "1e-100")]
    [InlineData(false, "1e100")]
    [InlineData(true, "1e-100")]
    [InlineData(true, "1e100")]
    public async Task PowerAndAccelerationKeyboardDraftsRetainFiniteDoubleExponentAfterBlur(bool ordered, string rawText)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(ordered ? AnimationProperty.MASK_RECTANGLE_TOP_LEFT : AnimationProperty.OPACITY);
        if (ordered)
        {
            session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(500, 500) });
            session.Editor.SetAnimationTransform(id, target, new ScenePoint(0, 0),
                new(Guid.NewGuid(), new(0), new(2), new ScenePoint(50, 60), 2));
            context.ViewModel.Effects.Target = target;
        }
        else
        {
            session.Editor.SetKeyframe(id, target, new(new(0), 0.5, KeyframeInterpolation.POWER));
            Assert.True(session.SelectKeyframe(new(id, target, new(0), new(0))));
        }
        Dispatcher.UIThread.RunJobs();
        UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var original = session.DocumentSnapshot;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, ordered ? "OperationAccelerationInput" : "PowerExponentInput");
        input.BringIntoView();
        context.Window.UpdateLayout();
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput(rawText);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(rawText, input.RawText);
        Assert.Same(original, session.DocumentSnapshot);
        var expected = double.Parse(rawText, System.Globalization.CultureInfo.InvariantCulture);
        var preview = Assert.Single(session.PreviewDocument.Layers[0].Tracks);
        Assert.Equal(expected, ordered ? Assert.Single(preview.Transforms).Acceleration : Assert.Single(preview.Keyframes).Exponent);
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();
        var track = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(expected, ordered ? Assert.Single(track.Transforms).Acceleration : Assert.Single(track.Keyframes).Exponent);
        Assert.Equal(expected, double.Parse(input.RawText, System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal(input.RawText, text.Text);
        Assert.True(session.Editor.Undo());
        Assert.Same(original, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task OrderedOperationNumericDraftSupportsNegativeTimeAndZeroAccelerationWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(500, 500) });
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(-1), new(2), new ScenePoint(50, 60), 2);
        session.Editor.SetAnimationTransform(id, target, new ScenePoint(0, 0), operation);
        context.ViewModel.Effects.Target = target;
        UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var original = session.DocumentSnapshot;
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationStartInput");
        input.BringIntoView();
        context.Window.UpdateLayout();
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("-0.5");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(-1, 2), session.PreviewDocument.Layers[0].Tracks[0].Transforms[0].Start);
        Assert.Same(original, session.DocumentSnapshot);
        context.ViewModel.Effects.OperationAcceleration.RawText = "0";
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var track = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(operation with { Start = new(-1, 2), Acceleration = 0 }, Assert.Single(track.Transforms));
        Assert.Empty(track.Keyframes);
        Assert.True(session.Editor.Undo());
        Assert.Same(original, session.DocumentSnapshot);
        Assert.False(UiTestActions.PropertyInput(context.Window, target.Property).IsEffectivelyEnabled);
    }
}
