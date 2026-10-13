using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class RotationVectorUiTests
{
    private static readonly string[] rotationFields = ["RotationXInput", "RotationYInput", "RotationInput"];

    [AvaloniaTheory]
    [InlineData("en-US", 270)]
    [InlineData("zh-CN", 320)]
    [InlineData("en-US", 650)]
    public async Task RotationComponentsShareOneVectorAndLongRawTextDoesNotExpandANarrowPanel(string language, double width)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var panel = context.Window.Panels["effects"];
        var originalParent = Assert.IsAssignableFrom<ContentControl>(panel.Parent);
        originalParent.Content = null;
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var host = new Window { Width = width, Height = 1000, Content = panel };
        try
        {
            host.Show();
            Localization.SetLanguage(language);
            context.ViewModel.Effects.TransformExpanded = true;
            context.ViewModel.Effects.RotationText = "123.1234567890123456789012345678";
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            var inputs = RotationInputs(panel);
            var vector = inputs[0].GetVisualAncestors().OfType<VectorDraftInput>().Single();
            Assert.All(inputs, input => Assert.Contains(vector, input.GetVisualAncestors()));
            Assert.All(inputs, input => Assert.Equal(48, input.MinWidth));
            var points = inputs.Select(input => input.TranslatePoint(default, host)!.Value).ToArray();
            Assert.Equal(points[0].Y, points[1].Y);
            Assert.Equal(points[1].Y, points[2].Y);
            Assert.All(inputs, input => Assert.True(input.Bounds.Width >= 48));
            Assert.True(points[2].X + inputs[2].Bounds.Width <= host.ClientSize.Width);
            var source = context.Session.DocumentSnapshot;
            Assert.Equal("123.1234567890123456789012345678", inputs[2].RawText);
            Assert.Equal(inputs[2].RawText, Assert.Single(inputs[2].GetVisualDescendants().OfType<TextBox>()).Text);
            Assert.True(inputs[2].FocusInput());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("123.1234567890123456789012345678", context.ViewModel.Effects.RotationText);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Effects.RestoreField("RotationInput");
            host.Content = null;
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            host.Close();
            originalParent.Content = panel;
        }
    }

    [AvaloniaTheory]
    [InlineData(0, "RotationXInput", "35")]
    [InlineData(1, "RotationYInput", "-42")]
    [InlineData(2, "RotationInput", "18")]
    public async Task EveryAxisUsesTheSharedRangeTrackAndKeyframeButton(int component, string field, string value)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context, text: "ABCD");
        var session = context.Session;
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        session.Editor.SetSubtitleAnimationRange(session.SelectedCue!.Id, range);
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D, TextRangeId: range.Id);
        context.ViewModel.Effects.Target = target;
        context.ViewModel.Effects.TransformExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, field);
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput(value);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(context.ViewModel.Effects.RotationRows);
        var button = context.Window.Panels["effects"].GetVisualDescendants().OfType<EffectPropertyRowView>()
            .Single(view => ReferenceEquals(view.DataContext, row)).GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, row.AddKeyframeCommand));
        button.BringIntoView();
        context.Window.UpdateLayout();
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command).ExecutionTask!;
        Dispatcher.UIThread.RunJobs();

        var track = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(target, track.Target);
        var frame = Assert.Single(track.Keyframes);
        Assert.True(frame.Value.IsVector3);
        Assert.Equal(double.Parse(value, CultureInfo.InvariantCulture), frame.Value.GetComponent(component));
        Assert.All(Enumerable.Range(0, 3).Where(index => index != component), index => Assert.Equal(0, frame.Value.GetComponent(index)));
        Assert.Equal(0, session.SelectedLayer.Transform.RotationX);
        Assert.Equal(0, session.SelectedLayer.Transform.RotationY);
        Assert.Equal(0, session.SelectedLayer.Transform.Rotation);
    }

    [AvaloniaFact]
    public async Task AllAxesReloadRoundTripPrecisionAndEscapeRestoresOnlyTheInvalidComponent()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        const double angle = 10.123456789012345;
        context.Session.Editor.Apply("Precise rotation", document => document with
        {
            Layers = [document.Layers[0] with { Transform = document.Layers[0].Transform with { RotationX = angle, RotationY = angle, Rotation = angle } }]
        });
        context.ViewModel.Effects.TransformExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var panel = context.Window.Panels["effects"];
        var inputs = RotationInputs(panel);
        Assert.All(inputs, input => Assert.Equal(angle, double.Parse(input.RawText, CultureInfo.CurrentCulture)));
        var source = context.Session.DocumentSnapshot;
        var xBox = Assert.Single(inputs[0].GetVisualDescendants().OfType<TextBox>());
        Assert.True(xBox.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("7e-");
        inputs[1].RawText = "19";
        Assert.False(context.Session.TryCommitDrafts());
        Dispatcher.UIThread.RunJobs();
        Assert.True(xBox.IsFocused);
        Assert.Equal("7e-", xBox.Text);
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(angle, double.Parse(inputs[0].RawText, CultureInfo.CurrentCulture));
        Assert.Equal("19", inputs[1].RawText);
        Assert.Same(source, context.Session.DocumentSnapshot);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(angle, context.Session.SelectedLayer!.Transform.RotationX);
        Assert.Equal(19, context.Session.SelectedLayer.Transform.RotationY);
        Assert.Equal(angle, context.Session.SelectedLayer.Transform.Rotation);
    }

    private static NumericDraftInput[] RotationInputs(Control panel)
    {
        return rotationFields.Select(name =>
            panel.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.Name == name)).ToArray();
    }
}
