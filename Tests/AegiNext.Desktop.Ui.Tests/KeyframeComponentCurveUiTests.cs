using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KeyframeComponentCurveUiTests
{
    [AvaloniaTheory]
    [InlineData(AnimationProperty.POSITION, 2)]
    [InlineData(AnimationProperty.ROTATION_3D, 3)]
    [InlineData(AnimationProperty.FILL, 4)]
    public async Task RealPickerEditsOneCurveAndKeepsOtherComponentsAtTheSharedKeyframe(AnimationProperty property, int dimensions)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var value = property == AnimationProperty.ROTATION_3D ? AnimationValue.FromVector3(new(10, 20, 30)) :
            property == AnimationProperty.FILL ? AnimationValue.FromColor(new(0.1f, 0.2f, 0.3f, 0.5f)) : AnimationValue.FromVector(new(10, 20));
        var frame = new Keyframe(new(1), value, KeyframeInterpolation.POWER)
        {
            Exponent = 2, CurveStart = 0.2, CurveEnd = 0.8, Reverse = true
        };
        var layer = context.Session.SelectedLayer!;
        context.Session.Editor.SetKeyframe(layer.Id, property, frame);
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, property, frame.Time, frame.Time)));
        context.ViewModel.Effects.AnimationExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var picker = UiTestActions.Find<ComboBox>(context.Window, "KeyframeCurveComponentCombo");
        Assert.True(picker.IsEffectivelyEnabled);
        Assert.Equal(dimensions + 1, picker.ItemCount);
        var source = context.Session.DocumentSnapshot;
        picker.SelectedItem = context.ViewModel.Effects.KeyframeCurveComponents.Single(choice => choice.Mask == 1 << (dimensions - 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(source, context.Session.DocumentSnapshot);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        input.BringIntoView();
        context.Window.UpdateLayout();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("7");
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        var track = Assert.Single(context.Session.SelectedLayer!.Tracks);
        var changed = Assert.Single(track.Keyframes);
        Assert.Equal(frame.Time, changed.Time);
        Assert.Equal(frame.Value, changed.Value);
        Assert.Equal(7, changed.GetCurve(dimensions - 1).Exponent);
        for (var component = 0; component < dimensions - 1; component++)
        {
            Assert.Equal(frame.GetCurve(component), changed.GetCurve(component));
        }
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task OrderedCurveDetailsExposeRealInterpolationReverseAndPowerAvailability()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var curve = new AnimationCurve(KeyframeInterpolation.LINEAR, 0.2, 0.8) { Exponent = 3, Reverse = true };
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(4), AnimationValue.FromVector3(new(10, 20, 30)), 7)
        {
            ComponentMask = 4, Curve = curve
        };
        var layer = context.Session.SelectedLayer!;
        context.Session.Editor.UpdateLayer(layer.Id, current => current with
        {
            Tracks = [new(AnimationProperty.ROTATION_3D, []) { InitialValue = AnimationValue.FromVector3(default), Transforms = [operation] }]
        });
        context.ViewModel.Effects.Target = new(AnimationProperty.ROTATION_3D);
        context.ViewModel.Effects.AnimationExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var exponent = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationAccelerationInput");
        var interpolation = UiTestActions.Find<ComboBox>(context.Window, "OperationCurveInterpolationCombo");
        var reverse = UiTestActions.Find<CheckBox>(context.Window, "OperationCurveReverseCheck");
        Assert.False(exponent.IsEffectivelyEnabled);
        Assert.Equal("3", exponent.RawText);
        Assert.True(reverse.IsChecked);
        var source = context.Session.DocumentSnapshot;
        interpolation.SelectedIndex = (int)KeyframeInterpolation.POWER;
        reverse.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(exponent.IsEffectivelyEnabled);
        exponent.RawText = "5";
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((int)KeyframeInterpolation.POWER, interpolation.SelectedIndex);
        Assert.Equal("5", exponent.RawText);
        Assert.False(reverse.IsChecked);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.True(context.Session.TryCommitDrafts(false));

        var changed = Assert.Single(Assert.Single(context.Session.SelectedLayer!.Tracks).Transforms);
        Assert.Equal(curve with { Interpolation = KeyframeInterpolation.POWER, Exponent = 5, Reverse = false }, changed.Curve);
        Assert.Equal(7, changed.Acceleration);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
    }
}
