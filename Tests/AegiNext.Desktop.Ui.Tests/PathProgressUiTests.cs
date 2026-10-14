using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证路径进度在特效面板的发现、真实加帧和顺序操作入口。</summary>
public sealed class PathProgressUiTests
{
    /// <summary>未启用进度轨时数字禁用，有路径才能加帧，路径区保持入口和说明可见。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressRowRemainsDiscoverableAndOnlyAPathCanEnableItsFirstKeyframe(bool hasPath)
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, hasPath);
        var source = context.Session.DocumentSnapshot;
        var model = ProgressModel(context);
        var control = ProgressControl(context, model);
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.IsScalar);
        Assert.False(control.CanEdit);
        Assert.Equal(hasPath, control.CanAddKeyframe);
        Assert.False(control.CanReset);
        Assert.False(Input(control).IsEffectivelyEnabled);
        Assert.Equal(hasPath, Action(control, model.AddKeyframeCommand).IsEffectivelyEnabled);
        Assert.False(Action(control, model.ResetCommand).IsEffectivelyEnabled);
        Assert.False(string.IsNullOrWhiteSpace(control.EditingHint));
        Assert.Contains(control.GetVisualAncestors(), ancestor => ancestor is Expander { Name: "PathCategory" });
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    /// <summary>真实加帧启用普通进度输入，键盘预览不写工程，Enter 一次提交且 Undo 恢复。</summary>
    [AvaloniaFact]
    public async Task FirstKeyframeButtonEnablesProgressAndKeyboardEditingPreviewsAndCommitsWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, true);
        var session = context.Session;
        await session.SeekFromUserAsync(new(1));
        var model = ProgressModel(context);
        var control = ProgressControl(context, model);
        var source = session.DocumentSnapshot;
        var add = Action(control, model.AddKeyframeCommand);

        UiTestActions.Click(context.Window, add);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(add.Command).ExecutionTask!;
        Flush(context.Window);

        var target = new AnimationTrackTarget(AnimationProperty.PATH_PROGRESS);
        var seeded = session.DocumentSnapshot;
        var track = Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == target);
        Assert.Equal(0.25, Assert.Single(track.Keyframes, frame => frame.Time == new MediaTime(1)).Value.Scalar, 10);
        model = ProgressModel(context);
        control = ProgressControl(context, model);
        Assert.True(control.CanEdit);
        var input = Input(control);
        input.BringIntoView();
        Flush(context.Window);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        try
        {
            Type(context.Window, text, "0.8");
            var preview = session.PreviewDocument.Layers[0].Tracks.Single(track => track.Target == target);
            Assert.Equal(0.8, Assert.Single(preview.Keyframes, frame => frame.Time == new MediaTime(1)).Value.Scalar);
            Assert.Same(seeded, session.DocumentSnapshot);
            UiTestActions.Press(context.Window, Key.Enter);
            Flush(context.Window);
            Assert.Equal(0.8, Assert.Single(session.SelectedLayer!.Tracks.Single(track => track.Target == target).Keyframes,
                frame => frame.Time == new MediaTime(1)).Value.Scalar);
            Assert.Same(seeded.Layers[0].MotionPath, session.SelectedLayer.MotionPath);
            Assert.True(session.Editor.Undo());
            Assert.Same(seeded, session.DocumentSnapshot);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            model.Restore(model.XFieldKey);
            session.TryCommitDrafts(false);
        }
    }

    /// <summary>顺序进度操作锁住普通数值和关键帧按钮，设置弹窗仅修改冻结操作并一次撤销。</summary>
    [AvaloniaFact]
    public async Task OrderedProgressLocksOrdinaryActionsAndItsPopupEditsOnlyTheCapturedOperationWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, true);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.PATH_PROGRESS);
        var first = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), 0.75, 2);
        var second = new AnimationTransformOperation(Guid.NewGuid(), new(3), new(4), 1, 3);
        session.Editor.SetAnimationTransform(layerId, target, 0, first);
        session.Editor.SetAnimationTransform(layerId, target, 0, second);
        Assert.True(session.SelectKeyframe(new(layerId, target, first.Start, first.Start) { OperationId = first.Id }));
        var model = ProgressModel(context);
        var control = ProgressControl(context, model);
        Assert.False(control.CanEdit);
        Assert.False(Input(control).IsEffectivelyEnabled);
        Assert.False(Action(control, model.AddKeyframeCommand).IsEffectivelyEnabled);
        Assert.False(Action(control, model.ResetCommand).IsEffectivelyEnabled);
        var source = session.DocumentSnapshot;
        var button = UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.PATH_PROGRESS);
        var content = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(button.Flyout).Content);
        var popup = Assert.IsType<AnimationOptionsPopupViewModel>(content.DataContext);
        Assert.Equal(first.Id, popup.CapturedOperationId);
        Assert.Equal(target, popup.CapturedTarget.Target);
        var input = UiTestActions.Find<NumericDraftInput>(content, "OperationValueInput");
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        try
        {
            var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(text));
            Type(root, text, "0.9");
            context.ViewModel.Effects.OperationAcceleration.RawText = "0";
            context.ViewModel.Effects.OperationOrder.RawText = "2";
            Assert.Same(source, session.DocumentSnapshot);
            root.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            root.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(root);
            Flush(context.Window);
            var changed = Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == target);
            Assert.Equal(second, changed.Transforms[0]);
            Assert.Equal(first with { Value = 0.9, Acceleration = 0 }, changed.Transforms[1]);
            Assert.Empty(changed.Keyframes);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Effects.RestoreOperationField("OperationValueInput");
            context.ViewModel.Effects.RestoreOperationField("OperationAccelerationInput");
            context.ViewModel.Effects.RestoreOperationField("OperationOrderInput");
            session.TryCommitDrafts(false);
            button.Flyout!.Hide();
        }
    }

    private static async Task PrepareAsync(MainWindowTestContext context, bool hasPath)
    {
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context, duration: new(6));
        if (hasPath)
        {
            context.Session.Editor.UpdateLayer(context.Session.SelectedLayer!.Id, layer => layer with
            {
                MotionPath = new(new(new(0, 0), [new(new(80, -40), new(160, 70), new(240, 20))]), new(4))
            });
        }
        context.ViewModel.Effects.PathExpanded = true;
        context.Window.Layouts.Activate("effects");
        Flush(context.Window);
    }

    private static AnimationPropertyRowViewModel ProgressModel(MainWindowTestContext context) =>
        Assert.Single(context.ViewModel.Effects.PathRows, row => row.Target.Property == AnimationProperty.PATH_PROGRESS);

    private static AnimationPropertyRowControl ProgressControl(MainWindowTestContext context, AnimationPropertyRowViewModel model) =>
        Assert.Single(context.Window.Panels["effects"].GetVisualDescendants().OfType<AnimationPropertyRowControl>(),
            row => ReferenceEquals(row.DataContext, model));

    private static NumericDraftInput Input(AnimationPropertyRowControl row) =>
        Assert.Single(row.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == row.ScalarInputName);

    private static Button Action(AnimationPropertyRowControl row, System.Windows.Input.ICommand command) =>
        Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Command, command));

    private static void Type(TopLevel host, TextBox text, string value)
    {
        text.BringIntoView();
        Flush(host);
        Assert.True(text.Focus());
        host.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        host.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        host.KeyTextInput(value);
        Flush(host);
    }

    private static void Flush(TopLevel host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
