using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ThreeDimensionalEffectUiTests
{
    [AvaloniaTheory]
    [InlineData(AnimationProperty.ROTATION_X, "35", 35)]
    [InlineData(AnimationProperty.ROTATION_Y, "-42", -42)]
    [InlineData(AnimationProperty.PERSPECTIVE_DISTANCE, "540", 540)]
    public async Task RealRangeInputAndKeyframeButtonUseTheExactTargetAndTimelineMovesOnlyTime(
        AnimationProperty property, string text, double expected)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context, text: "ABCD");
        var session = context.Session;
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        session.Editor.SetSubtitleAnimationRange(session.SelectedCue!.Id, range);
        var target = new AnimationTrackTarget(property == AnimationProperty.PERSPECTIVE_DISTANCE ? property : AnimationProperty.ROTATION_3D, TextRangeId: range.Id);
        var component = property == AnimationProperty.ROTATION_Y ? 1 : 0;
        context.ViewModel.Effects.Target = target;
        context.ViewModel.Effects.TransformExpanded = true;
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var row = context.ViewModel.Effects.RotationRows.Concat(context.ViewModel.Effects.PerspectiveRows)
            .Single(row => row.Target == target);
        var rowView = FindRow(context, row);
        var input = rowView.GetVisualDescendants().OfType<AnimationPropertyRowControl>().Single()
            .FindFieldInput(component == 1 ? row.YFieldKey : row.XFieldKey)!;
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        input.BringIntoView();
        context.Window.UpdateLayout();
        var before = session.DocumentSnapshot;

        Assert.True(box.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(before, session.DocumentSnapshot);
        Assert.Equal(text, (component == 1 ? row.Y : row.X).RawText);
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        var editedRange = session.SelectedCue!.AnimationRanges[0];
        Assert.Equal(expected, RangeValue(editedRange, property));
        Assert.Equal(before.Layers[0].Transform, session.SelectedLayer!.Transform);
        Assert.Empty(session.SelectedLayer.Tracks);
        rowView = FindRow(context, row);
        var keyframeButton = rowView.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, row.AddKeyframeCommand));
        keyframeButton.BringIntoView();
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var point = keyframeButton.TranslatePoint(new(keyframeButton.Bounds.Width / 2, keyframeButton.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(row.AddKeyframeCommand).ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        var track = Assert.Single(session.SelectedLayer.Tracks);
        Assert.Equal(target, track.Target);
        var frame = Assert.Single(track.Keyframes);
        Assert.Equal(expected, frame.Value.GetComponent(component));
        var keyed = session.DocumentSnapshot;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        Assert.Contains(target, timeline.GetAnimationTargets(session.SelectedLayer.Id));
        var keyPoint = timeline.TranslatePoint(timeline.GetKeyframePoint(session.SelectedLayer.Id, target, frame.Time, frame.Value)!.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(keyPoint));

        context.Window.MouseDown(keyPoint, MouseButton.Left);
        context.Window.MouseMove(keyPoint + new Vector(timeline.PixelsPerSecond, 0), RawInputModifiers.LeftMouseButton);
        context.Window.MouseUp(keyPoint + new Vector(timeline.PixelsPerSecond, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var moved = Assert.Single(Assert.Single(session.SelectedLayer.Tracks).Keyframes);
        Assert.Equal(frame.Time + new AegiNext.Core.Timing.MediaTime(1), moved.Time);
        Assert.Equal(frame.Value, moved.Value);
        Assert.Equal(frame.Interpolation, moved.Interpolation);
        Assert.True(session.Editor.Undo());
        Assert.Same(keyed, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task SpatialRowsTranslateLiveKeepRawDraftAndExpandTheCorrectCategoryForErrors()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var effects = context.ViewModel.Effects;
        var row = Assert.Single(effects.RotationRows);
        var source = context.Session.DocumentSnapshot;
        try
        {
            row.X.RawText = "7e-";
            effects.TransformExpanded = false;
            Assert.False(context.Session.TryCommitDrafts());
            Dispatcher.UIThread.RunJobs();
            Assert.True(effects.TransformExpanded);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            context.Window.UpdateLayout();

            Assert.Equal("7e-", row.X.RawText);
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.Equal("旋转", row.Name);
            var distance = Assert.Single(effects.PerspectiveRows);
            Assert.Equal("透视距离", distance.Name);
            Assert.Equal(Localization.Get("Workbench.PerspectiveDistanceHint"), distance.EditingHint);
            var view = FindRow(context, row);
            var input = view.GetVisualDescendants().OfType<AnimationPropertyRowControl>().Single().FindFieldInput(row.XFieldKey)!;
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            input.BringIntoView();
            context.Window.UpdateLayout();
            Assert.True(box.Focus());
            UiTestActions.Press(context.Window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0", row.X.RawText);
            Assert.False(context.Session.HasProjectDrafts);
        }
        finally
        {
            effects.RestorePropertyField(row.XFieldKey);
        }
    }

    private static EffectPropertyRowView FindRow(MainWindowTestContext context, AnimationPropertyRowViewModel row)
    {
        return context.Window.Panels["effects"].GetVisualDescendants().OfType<EffectPropertyRowView>()
            .Single(view => ReferenceEquals(view.DataContext, row));
    }

    private static double RangeValue(SubtitleAnimationRange range, AnimationProperty property) => property switch
    {
        AnimationProperty.ROTATION_X => range.RotationX,
        AnimationProperty.ROTATION_Y => range.RotationY,
        _ => range.PerspectiveDistance
    };
}
