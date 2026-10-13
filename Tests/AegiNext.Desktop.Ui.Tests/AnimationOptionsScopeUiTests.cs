using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证实际属性弹窗在空工程、完整目标切换和蒙版有序草稿恢复中的身份约束。</summary>
public sealed class AnimationOptionsScopeUiTests
{
    /// <summary>无媒体工程通过真实指针打开两类属性按钮，关闭和重开均保持目标及工程快照。</summary>
    [AvaloniaTheory]
    [InlineData(AnimationProperty.POSITION)]
    [InlineData(AnimationProperty.ROTATION_3D)]
    public async Task PointerOpeningWithoutMediaPreservesTheTargetAndReportsTheFlyoutLifecycle(AnimationProperty property)
    {
        await using var context = new MainWindowTestContext();
        await UiTestActions.CreateSubtitleAsync(context, text: "YOSYMPH666");
        Assert.Null(context.Controller.Snapshot.FilePath);
        UiTestActions.ExpandEffectsCategory(context.Window, "TransformCategory");
        var button = FindSettingsButton(context, property);
        var opened = 0;
        var closed = 0;
        button.PropertyChanged += (_, change) =>
        {
            if (change.Property == Button.FlyoutProperty && change.NewValue is Flyout flyout)
            {
                flyout.Opened += (_, _) => opened++;
                flyout.Closed += (_, _) => closed++;
            }
        };
        var source = context.Session.DocumentSnapshot;
        var target = new AnimationTrackTarget(property);
        var layerId = context.Session.SelectedLayer!.Id;
        Assert.Null(button.Flyout);

        ClickVisibleButton(context.Window, button);

        var popup = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(popup.IsOpen);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        var captureDirectory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            Assert.True(Path.IsPathFullyQualified(captureDirectory));
            Directory.CreateDirectory(captureDirectory);
            var popupRoot = TopLevel.GetTopLevel(Assert.IsType<AnimationOptionsPopupView>(popup.Content));
            Assert.NotNull(popupRoot);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var screenshot = popupRoot.CaptureRenderedFrame();
            Assert.NotNull(screenshot);
            screenshot.Save(Path.Combine(captureDirectory, $"animation-options-{property}.png"), PngBitmapEncoderOptions.Default);
        }
        var model = PopupModel(button);
        Assert.Equal(layerId, model.CapturedTarget.LayerId);
        Assert.Equal(target, model.CapturedTarget.Target);
        Assert.Equal(MediaTime.Zero, model.CapturedTarget.LocalTime);
        Assert.False(model.CapturedTarget.IsKeyframe);
        Assert.True(model.NeedsAnimationKeyframe);
        Assert.False(UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo").IsEffectivelyEnabled);
        Assert.Same(source, model.SourceSnapshot);
        Assert.Same(source, context.Session.DocumentSnapshot);
        popup.Hide();
        Flush(context.Window);
        Assert.False(popup.IsOpen);
        Assert.Equal(1, closed);
        Assert.Null(context.Session.AnimationOptionsTarget);

        ClickVisibleButton(context.Window, button);

        var reopened = PopupModel(button);
        Assert.True(popup.IsOpen);
        Assert.NotSame(model, reopened);
        Assert.Equal(model.CapturedTarget, reopened.CapturedTarget);
        Assert.True(opened >= 2);
        Assert.Equal(closed + 1, opened);
        Assert.Same(source, context.Session.DocumentSnapshot);
        popup.Hide();
        Assert.Equal(opened, closed);
    }

    /// <summary>范围、外观状态或图层切换关闭原弹窗，旧事件不能修改新目标，真实新编辑支持撤销。</summary>
    [AvaloniaTheory]
    [InlineData("range")]
    [InlineData("state")]
    [InlineData("layer")]
    public async Task ChangingTheCompleteTargetRejectsOldPopupEventsAndEditsOnlyTheNewTrack(string change)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = await UiTestActions.CreateSubtitleAsync(context, text: "ABCDE");
        var session = context.Session;
        var firstRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var secondRange = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        session.Editor.SetSubtitleAnimationRange(cueId, firstRange);
        session.Editor.SetSubtitleAnimationRange(cueId, secondRange);
        var firstLayerId = session.SelectedLayer!.Id;
        var oldTarget = new AnimationTrackTarget(AnimationProperty.FILL_BLUR, TextRangeId: firstRange.Id);
        var newTarget = change switch
        {
            "range" => oldTarget with { TextRangeId = secondRange.Id },
            "state" => oldTarget with { State = SubtitleAnimationState.ACTIVE },
            _ => new AnimationTrackTarget(AnimationProperty.FILL_BLUR)
        };
        var secondCueId = change == "layer" ? session.Editor.AddSubtitle(new(6), new(11), "Other layer") : cueId;
        var secondLayerId = session.DocumentSnapshot.Layers.Single(layer => layer.SubtitleId == secondCueId).Id;
        session.Editor.SetKeyframe(firstLayerId, oldTarget, new(new(1), 4, KeyframeInterpolation.POWER)
        {
            Exponent = 2
        });
        session.Editor.SetKeyframe(secondLayerId, newTarget, new(new(1), 8, KeyframeInterpolation.POWER)
        {
            Exponent = 5
        });
        session.SelectCue(cueId);
        Assert.True(session.SelectKeyframe(new(firstLayerId, oldTarget, new(1), new(1))));
        var oldButton = UiTestActions.OpenAnimationOptions(context.Window, oldTarget.Property);
        var oldView = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(oldButton.Flyout).Content);
        var oldModel = PopupModel(oldButton);
        var oldInput = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        var source = session.DocumentSnapshot;

        if (change == "layer")
        {
            session.SelectCue(secondCueId);
        }
        else if (change == "range")
        {
            UiTestActions.Find<ComboBox>(context.Window, "EffectScopeCombo").SelectedItem =
                context.ViewModel.Effects.Scopes.Single(scope => scope.Id == secondRange.Id);
        }
        else
        {
            UiTestActions.Find<ComboBox>(context.Window, "EffectStateCombo").SelectedItem =
                context.ViewModel.Effects.States.Single(state => state.State == SubtitleAnimationState.ACTIVE);
        }
        Flush(context.Window);

        Assert.False(oldButton.Flyout!.IsOpen);
        Assert.Null(oldView.DataContext);
        Assert.Equal(secondLayerId, session.SelectedLayerId);
        Assert.Equal(newTarget, session.SceneEditing.Target);
        Assert.Same(source, session.DocumentSnapshot);
        Assert.True(session.SelectKeyframe(new(secondLayerId, newTarget, new(1), new(1))));
        var newButton = UiTestActions.OpenAnimationOptions(context.Window, newTarget.Property);
        var newModel = PopupModel(newButton);
        Assert.Equal(secondLayerId, newModel.CapturedTarget.LayerId);
        Assert.Equal(newTarget, newModel.CapturedTarget.Target);
        Assert.Equal(new MediaTime(1), newModel.CapturedTarget.LocalTime);
        Assert.True(newModel.CapturedTarget.IsKeyframe);
        Assert.NotSame(oldModel, newModel);
        var interpolation = UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo");
        var newInput = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");

        oldModel.Interpolation = (int)KeyframeInterpolation.HOLD;
        oldInput.RawText = "99";
        Assert.False(oldModel.TryCommitDrafts(false));
        Flush(context.Window);

        Assert.Equal((int)KeyframeInterpolation.POWER, interpolation.SelectedIndex);
        Assert.Equal("5", newInput.RawText);
        Assert.Same(source, session.DocumentSnapshot);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        Flush(context.Window);
        var original = Assert.Single(session.DocumentSnapshot.Layers.Single(layer => layer.Id == firstLayerId)
            .Tracks.Single(track => track.Target == oldTarget).Keyframes);
        var edited = Assert.Single(session.DocumentSnapshot.Layers.Single(layer => layer.Id == secondLayerId)
            .Tracks.Single(track => track.Target == newTarget).Keyframes);
        Assert.Equal(KeyframeInterpolation.POWER, original.Interpolation);
        Assert.Equal(2, original.Exponent);
        Assert.Equal(4, original.Value.Scalar);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, edited.Interpolation);
        Assert.Equal(8, edited.Value.Scalar);
        Assert.True(session.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
    }

    /// <summary>关闭蒙版有序弹窗后的无效草稿重开原节点、操作和字段，Esc只恢复该字段。</summary>
    [AvaloniaFact]
    public async Task InvalidOrderedMaskDraftReopensItsNodeOperationAndFieldAndCommitsWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var node = new MaskNode
        {
            Position = new(10, 20)
        };
        var otherNode = new MaskNode
        {
            Position = new(30, 40)
        };
        session.Editor.SetClipMask(layerId, new VectorClipMask
        {
            Contours =
            [
                new()
                {
                    Nodes = [node, otherNode]
                }
            ]
        });
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id);
        var first = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(1), new ScenePoint(50, 60), 2);
        var second = new AnimationTransformOperation(Guid.NewGuid(), new(2), new(3), new ScenePoint(80, 90), 3);
        session.Editor.SetAnimationTransform(layerId, target, node.Position, first);
        session.Editor.SetAnimationTransform(layerId, target, node.Position, second);
        Assert.True(session.SelectKeyframe(new(layerId, target, second.Start, second.Start)
        {
            OperationId = second.Id
        }));
        var button = UiTestActions.OpenAnimationOptions(context.Window, target.Property, "masks");
        var source = session.DocumentSnapshot;
        try
        {
            Assert.Equal(second.Id, PopupModel(button).CapturedOperationId);
            var input = OperationY(context);
            Assert.True(input.FocusInput());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("9e-");
            context.ViewModel.Effects.OperationStart.RawText = "2.5";
            Assert.False(session.TryCommitDrafts(false));
            Assert.Equal("masks", context.ViewModel.InvalidPanelId);
            Assert.Equal("OperationValueYInput", context.ViewModel.InvalidFieldKey);
            button.Flyout!.Hide();
            await session.SeekFromUserAsync(new(4));
            Assert.Equal("9e-", context.ViewModel.Effects.OperationValueY.RawText);
            Assert.Equal("2.5", context.ViewModel.Effects.OperationStart.RawText);
            Assert.Same(source, session.DocumentSnapshot);

            Assert.False(session.TryCommitDrafts());
            Flush(context.Window);

            Assert.True(button.Flyout.IsOpen);
            Assert.Equal("masks", session.AnimationOptionsPanelId);
            Assert.Equal(node.Id, session.SceneEditing.MaskNodeId);
            var restored = PopupModel(button);
            Assert.Equal(layerId, restored.CapturedTarget.LayerId);
            Assert.Equal(target, restored.CapturedTarget.Target);
            Assert.Equal(second.Id, restored.CapturedOperationId);
            Assert.Equal(second.Id, session.SceneEditing.TransformOperationId);
            input = OperationY(context);
            Assert.Equal("9e-", input.RawText);
            Assert.True(Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).IsFocused);
            Assert.True(DataValidationErrors.GetHasErrors(input));
            UiTestActions.Press(context.Window, Key.Escape);
            Flush(context.Window);
            Assert.Equal("90", input.RawText);
            Assert.Equal("2.5", context.ViewModel.Effects.OperationStart.RawText);
            Assert.Same(source, session.DocumentSnapshot);
            Assert.True(input.FocusInput());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("110");
            UiTestActions.Press(context.Window, Key.Enter);
            Flush(context.Window);

            var operations = Assert.Single(session.SelectedLayer!.Tracks).Transforms;
            Assert.Equal(first, operations[0]);
            Assert.Equal(second.Id, operations[1].Id);
            Assert.Equal(new MediaTime(5, 2), operations[1].Start);
            Assert.Equal(new ScenePoint(80, 110), operations[1].Value.Vector);
            Assert.Equal(otherNode, Assert.IsType<VectorClipMask>(session.SelectedLayer.Mask).Contours[0].Nodes[1]);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Effects.RestoreOperationField("OperationValueYInput");
            context.ViewModel.Effects.RestoreOperationField("OperationStartInput");
            session.TryCommitDrafts(false);
            button.Flyout?.Hide();
        }
    }

    private static NumericDraftInput OperationY(MainWindowTestContext context) =>
        UiTestActions.Find<VectorDraftInput>(context.Window, "OperationVectorInput").FindFieldInput("OperationValueYInput")!;

    private static AnimationOptionsPopupViewModel PopupModel(Button button) =>
        Assert.IsType<AnimationOptionsPopupViewModel>(Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(button.Flyout).Content).DataContext);

    private static Button FindSettingsButton(MainWindowTestContext context, AnimationProperty property)
    {
        var panel = context.Window.Panels["effects"];
        var row = panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>()
            .FirstOrDefault(control => control.DataContext is AnimationPropertyRowViewModel model && model.Target.Property == property);
        return row is not null ? row.FindControl<Button>("AnimationOptionsButton")! : panel.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("animation-options") && Equals(button.Tag, property));
    }

    private static void ClickVisibleButton(Window window, Button button)
    {
        button.BringIntoView();
        Flush(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        Assert.True(window.InputHitTest(point) is Visual hit && (ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button)));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
