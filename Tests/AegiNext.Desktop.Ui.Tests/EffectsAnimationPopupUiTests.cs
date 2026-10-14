using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsAnimationPopupUiTests
{
    [AvaloniaFact]
    public async Task PropertyEditingUsesSettingsButtonsAndHidesAbsentMasksWithoutADetailsSection()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var effects = context.Window.Panels["effects"];
        Assert.DoesNotContain(effects.GetVisualDescendants().OfType<Control>(), control => control.Name == "AnimationCategory");
        Assert.False(UiTestActions.Find<Expander>(context.Window, "MaskCategory").IsVisible);
        var row = effects.GetVisualDescendants().OfType<AnimationPropertyRowControl>().First(control => control.ShowAnimationActions);
        var settings = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Name == "AnimationOptionsButton");
        Assert.Equal(32, settings.Width);
        Assert.Equal(32, settings.Height);
    }

    [AvaloniaFact]
    public async Task PopupWithoutAKeyframeDoesNotWriteTheProjectAndDisablesCurveEditing()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var source = context.Session.DocumentSnapshot;
        var button = UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.FILL_BLUR);
        Assert.False(UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo").IsEffectivelyEnabled);
        Assert.Same(source, context.Session.DocumentSnapshot);
        button.Flyout!.Hide();
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task OrdinaryColorInterpolationRequiresAnExactFrame()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.FILL);
        session.Editor.SetKeyframe(layerId, target, new(new(1), SceneColor.White));
        session.Editor.SetKeyframe(layerId, target, new(new(3), SceneColor.Black));
        await session.SeekFromUserAsync(new(2));
        var source = session.DocumentSnapshot;
        UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var colors = UiTestActions.Find<ComboBox>(context.Window, "ColorInterpolationSpaceCombo");
        Assert.False(colors.IsEffectivelyEnabled);
        colors.SelectedIndex = (int)AnimationColorSpace.SRGB;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(source, session.DocumentSnapshot);
        Assert.Equal(AnimationColorSpace.LINEAR_RGB, Assert.Single(session.SelectedLayer!.Tracks).ColorSpace);
    }

    [AvaloniaFact]
    public async Task ExactKeyframePopupEditsTheCurveAndRowDeleteNeverFallsBackToTheSubtitle()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var layer = context.Session.SelectedLayer!;
        var target = new AnimationTrackTarget(AnimationProperty.FILL_BLUR);
        context.Session.Editor.SetKeyframe(layer.Id, target, new(new(1), 4));
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, target, new(1), new(1))));
        var button = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var source = context.Session.DocumentSnapshot;
        var interpolation = UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo");
        Assert.True(interpolation.IsEffectivelyEnabled);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(KeyframeInterpolation.EASE_OUT, Assert.Single(context.Session.SelectedLayer!.Tracks).Keyframes[0].Interpolation);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(source, context.Session.DocumentSnapshot);
        button.Flyout!.Hide();
        Assert.True(button.Focus());
        UiTestActions.Press(context.Window, Key.Delete);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Empty(context.Session.SelectedLayer!.Tracks);
        var deleted = context.Session.DocumentSnapshot;
        UiTestActions.Press(context.Window, Key.Delete);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Same(deleted, context.Session.DocumentSnapshot);
        Assert.Single(deleted.Subtitles);
    }

    [AvaloniaFact]
    public async Task ReopeningTheSameButtonCapturesTheNewKeyframeAndCommitsItsCurve()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.FILL_BLUR);
        session.Editor.SetKeyframe(layerId, target, new(new(1), 4));
        session.Editor.SetKeyframe(layerId, target, new(new(3), 8));
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        var settings = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo").SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        Dispatcher.UIThread.RunJobs();
        var firstView = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(settings.Flyout).Content);
        settings.Flyout!.Hide();
        Assert.False(settings.Flyout.IsOpen);
        Assert.Null(firstView.DataContext);
        Assert.True(session.SelectKeyframe(new(layerId, target, new(3), new(3))));
        var source = session.DocumentSnapshot;
        Assert.Same(settings, UiTestActions.OpenAnimationOptions(context.Window, target.Property));
        var secondView = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(settings.Flyout).Content);
        Assert.NotSame(firstView, secondView);
        var model = Assert.IsType<AnimationOptionsPopupViewModel>(secondView.DataContext);
        Assert.Equal(new MediaTime(3), model.CapturedTarget.LocalTime);
        Assert.Same(source, session.DocumentSnapshot);
        Assert.Same(source, model.SourceSnapshot);
        var interpolation = UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo");
        Assert.True(interpolation.IsEffectivelyEnabled);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.HOLD;
        Dispatcher.UIThread.RunJobs();
        var frames = Assert.Single(session.SelectedLayer!.Tracks).Keyframes;
        Assert.Equal(KeyframeInterpolation.EASE_OUT, frames[0].Interpolation);
        Assert.Equal(KeyframeInterpolation.HOLD, frames[1].Interpolation);
    }

    [AvaloniaFact]
    public async Task InvalidPopupDraftSurvivesClosingAndRestoresOrCommitsToTheFrozenKeyframe()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.FILL_BLUR);
        session.Editor.SetKeyframe(layerId, target, new(new(1), 4, KeyframeInterpolation.POWER) { Exponent = 2 });
        session.Editor.SetKeyframe(layerId, target, new(new(3), 8, KeyframeInterpolation.POWER) { Exponent = 4 });
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        var source = session.DocumentSnapshot;
        var button = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        Assert.True(input.FocusInput());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("2e-");
        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal("2e-", context.ViewModel.Effects.PowerExponent.RawText);
        Assert.Equal(new MediaTime(1), session.SceneEditing.DraftTarget!.LocalTime);
        button.Flyout!.Hide();
        await session.SeekFromUserAsync(new(3));
        Assert.Equal("2e-", context.ViewModel.Effects.PowerExponent.RawText);
        Assert.Same(source, session.DocumentSnapshot);

        Assert.False(session.TryCommitDrafts());
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.Flyout.IsOpen);
        input = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        Assert.Equal(new MediaTime(1), session.AnimationOptionsTarget!.LocalTime);
        Assert.True(Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).IsFocused);
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2", input.RawText);
        Assert.Same(source, session.DocumentSnapshot);

        Assert.True(input.FocusInput());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("3.5");
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        var keys = Assert.Single(session.SelectedLayer!.Tracks).Keyframes;
        Assert.Equal(3.5, keys[0].Exponent);
        Assert.Equal(4, keys[1].Exponent);
        Assert.True(session.Editor.Undo());
        Assert.Same(source, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task MaskSettingsUseTheSamePopupAndDeleteOnlyTheSelectedNodeKeyframe()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        session.Editor.SetClipMask(layerId, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id);
        session.Editor.SetKeyframe(layerId, firstTarget, new(new(1), first.Position));
        session.Editor.SetKeyframe(layerId, secondTarget, new(new(1), second.Position));
        var firstSettings = UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.FILL_BLUR);
        var sharedFlyout = Assert.IsType<Flyout>(firstSettings.Flyout);
        var originalContent = Assert.IsAssignableFrom<Control>(sharedFlyout.Content);
        var originalModel = Assert.IsType<AnimationOptionsPopupViewModel>(originalContent.DataContext);
        Assert.Equal(AnimationProperty.FILL_BLUR, originalModel.CapturedTarget.Target!.Value.Property);
        var source = session.DocumentSnapshot;
        Assert.True(session.SelectKeyframe(new(layerId, firstTarget, new(1), new(1))));
        var maskSettings = UiTestActions.OpenAnimationOptions(context.Window, firstTarget.Property);
        Assert.Same(sharedFlyout, maskSettings.Flyout);
        var maskContent = Assert.IsAssignableFrom<Control>(sharedFlyout.Content);
        Assert.NotSame(originalContent, maskContent);
        var maskModel = Assert.IsType<AnimationOptionsPopupViewModel>(maskContent.DataContext);
        Assert.NotSame(originalModel, maskModel);
        Assert.Equal(firstTarget, maskModel.CapturedTarget.Target);
        Assert.True(maskSettings.Flyout!.IsOpen);
        Assert.Equal("effects", session.AnimationOptionsPanelId);
        Assert.Equal(firstTarget, session.AnimationOptionsTarget!.Target);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        Assert.True(maskSettings.Flyout.IsOpen);
        Assert.True(maskSettings.IsAttachedToVisualTree());
        var openFlyouts = context.Window.Panels.Values.SelectMany(panel => panel.GetVisualDescendants().OfType<Button>())
            .Select(button => button.Flyout).OfType<Flyout>().Distinct().Where(flyout => flyout.IsOpen);
        Assert.Same(sharedFlyout, Assert.Single(openFlyouts));
        Assert.Same(source, session.DocumentSnapshot);
        Assert.True(UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo").IsEffectivelyEnabled);
        maskSettings.Flyout.Hide();
        Assert.True(maskSettings.Focus());
        UiTestActions.Press(context.Window, Key.Delete);
        await session.WaitForProjectIdleAsync();
        Assert.Equal(secondTarget, Assert.Single(session.SelectedLayer!.Tracks).Target);
        var deleted = session.DocumentSnapshot;
        UiTestActions.Press(context.Window, Key.Delete);
        await session.WaitForProjectIdleAsync();
        Assert.Same(deleted, session.DocumentSnapshot);
        Assert.Single(deleted.Subtitles);
    }

    [AvaloniaFact]
    public async Task MaskRefreshKeepsPopupAnchorsAndLabelsWhileTopologyChangesRebuildTheFields()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var category = UiTestActions.Find<Expander>(context.Window, "MaskCategory");
        Assert.False(category.IsVisible);
        session.Editor.SetClipMask(layerId, new RectangleClipMask { BottomRight = new(400, 300) });
        Assert.True(category.IsVisible);
        var geometry = context.ViewModel.Masks.GeometryFields;
        var settings = UiTestActions.OpenAnimationOptions(context.Window, AnimationProperty.MASK_POSITION);
        var row = settings.GetVisualAncestors().OfType<AnimationPropertyRowControl>().First();
        var sharedRow = Assert.IsType<AnimationPropertyRowViewModel>(row.DataContext);
        var source = session.DocumentSnapshot;
        Assert.True(session.TryCommitDrafts(false));
        session.MaskEditing.Refresh(true);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        Assert.Same(geometry, context.ViewModel.Masks.GeometryFields);
        Assert.True(settings.IsAttachedToVisualTree());
        Assert.True(settings.Flyout!.IsOpen);
        Localization.SetLanguage("zh-CN");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(geometry, context.ViewModel.Masks.GeometryFields);
        Assert.Equal(Localization.Get("Workbench.MASK_POSITION"), row.Label);
        Assert.Equal(sharedRow.AnimationOptionsHint, row.AnimationOptionsHint);
        Assert.True(settings.IsAttachedToVisualTree());
        Assert.True(settings.Flyout.IsOpen);
        Assert.Same(source, session.DocumentSnapshot);
        settings.Flyout.Hide();

        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        session.Editor.SetClipMask(layerId, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        Assert.NotSame(geometry, context.ViewModel.Masks.GeometryFields);
        Assert.Empty(context.ViewModel.Masks.GeometryFields);
        Assert.DoesNotContain(context.ViewModel.Effects.MaskRows, item => item.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        session.MaskEditing.SelectNode(first.Id);
        var firstFields = context.ViewModel.Masks.NodeFields;
        Assert.Equal(3, firstFields.Length);
        Assert.All(firstFields, item => Assert.Equal(first.Id, item.Target!.Value.NodeId));
        session.MaskEditing.SelectNode(second.Id);
        var secondFields = context.ViewModel.Masks.NodeFields;
        Assert.NotSame(firstFields, secondFields);
        Assert.All(secondFields, item => Assert.Equal(second.Id, item.Target!.Value.NodeId));
        session.Editor.ClearClipMask(layerId);
        Dispatcher.UIThread.RunJobs();
        Assert.False(category.IsVisible);
        Assert.Empty(context.ViewModel.Masks.GeometryFields);
        Assert.Empty(context.ViewModel.Masks.NodeFields);
        Assert.True(session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.True(category.IsVisible);
        Assert.True(context.ViewModel.Masks.IsVectorMask);
        Assert.Empty(context.ViewModel.Masks.GeometryFields);
        session.MaskEditing.SelectNode(second.Id);
        Assert.NotSame(secondFields, context.ViewModel.Masks.NodeFields);
        Assert.All(context.ViewModel.Masks.NodeFields, item => Assert.Equal(second.Id, item.Target!.Value.NodeId));
    }

    [AvaloniaFact]
    public async Task DeleteWhilePopupOrPropertyTextHasFocusKeepsTheSelectedKeyframe()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.FILL_BLUR);
        session.Editor.SetKeyframe(layerId, target, new(new(1), 4, KeyframeInterpolation.POWER) { Exponent = 2 });
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        var source = session.DocumentSnapshot;
        var settings = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        Assert.True(input.FocusInput());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        UiTestActions.Press(context.Window, Key.Delete);
        Assert.Same(source, session.DocumentSnapshot);
        UiTestActions.Press(context.Window, Key.Escape);
        settings.Flyout!.Hide();
        var propertyInput = UiTestActions.PropertyInput(context.Window, target.Property);
        Assert.True(propertyInput.FocusInput());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        UiTestActions.Press(context.Window, Key.Delete);
        Assert.Same(source, session.DocumentSnapshot);
        UiTestActions.Press(context.Window, Key.Escape);
        Assert.Single(Assert.Single(session.SelectedLayer!.Tracks).Keyframes);
    }

    [AvaloniaFact]
    public async Task SwitchingOrderedOperationsCommitsTheOldDraftAndRejectsEventsAfterExternalSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.BLUR);
        var first = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), 4, 2);
        var second = new AnimationTransformOperation(Guid.NewGuid(), new(2), new(4), 8, 3);
        session.Editor.SetAnimationTransform(layerId, target, 0, first);
        session.Editor.SetAnimationTransform(layerId, target, 0, second);
        var settings = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var options = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(settings.Flyout).Content);
        var model = Assert.IsType<AnimationOptionsPopupViewModel>(options.DataContext);
        Assert.Equal(target, model.CapturedTarget.Target);
        Assert.Equal(first.Id, model.CapturedOperationId);
        var value = UiTestActions.Find<NumericDraftInput>(context.Window, "OperationValueInput");
        value.RawText = "6";
        var picker = UiTestActions.Find<ComboBox>(context.Window, "TransformOperationCombo");
        picker.SelectedItem = model.TransformOperations.Single(operation => operation.Id == second.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.Flyout!.IsOpen);
        Assert.Equal(second.Id, model.CapturedOperationId);
        var track = Assert.Single(session.SelectedLayer!.Tracks);
        Assert.Equal(6, track.Transforms[0].Value.Scalar);
        Assert.Equal(8, track.Transforms[1].Value.Scalar);
        Assert.Equal("8", value.RawText);
        Assert.Same(session.DocumentSnapshot, model.SourceSnapshot);
        Assert.True(session.SelectKeyframe(new(layerId, target, first.Start, first.Start) { OperationId = first.Id }));
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.Flyout.IsOpen);
        var externalSelection = session.DocumentSnapshot;
        model.OperationCurveReverse = true;
        Assert.False(model.TryCommitDrafts(false));
        Assert.Same(externalSelection, session.DocumentSnapshot);
        Assert.Equal(first.Id, session.SceneEditing.TransformOperationId);
    }

    [AvaloniaFact]
    public async Task ClosingPreservesUncommittedTextAndDisposedControlsCannotEditAnotherProperty()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var firstTarget = new AnimationTrackTarget(AnimationProperty.FILL_BLUR);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.STROKE_BLUR);
        session.Editor.SetKeyframe(layerId, firstTarget, new(new(1), 4, KeyframeInterpolation.POWER) { Exponent = 2 });
        session.Editor.SetKeyframe(layerId, secondTarget, new(new(1), 8, KeyframeInterpolation.POWER) { Exponent = 5 });
        Assert.True(session.SelectKeyframe(new(layerId, firstTarget, new(1), new(1))));
        var settings = UiTestActions.OpenAnimationOptions(context.Window, firstTarget.Property);
        var oldView = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(settings.Flyout).Content);
        var oldInput = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        var source = session.DocumentSnapshot;
        oldInput.RawText = "3.5";
        settings.Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("3.5", context.ViewModel.Effects.PowerExponent.RawText);
        Assert.Same(source, session.DocumentSnapshot);
        Assert.Null(oldView.DataContext);
        Assert.True(session.TryCommitDrafts(false));
        Assert.True(session.SelectKeyframe(new(layerId, secondTarget, new(1), new(1))));
        UiTestActions.OpenAnimationOptions(context.Window, secondTarget.Property);
        var newInput = UiTestActions.Find<NumericDraftInput>(context.Window, "PowerExponentInput");
        var changedTarget = session.DocumentSnapshot;
        oldInput.RawText = "99";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("5", newInput.RawText);
        Assert.Equal("5", context.ViewModel.Effects.PowerExponent.RawText);
        Assert.Same(changedTarget, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task DeleteTransformCommitsALegalPendingDraftAndDeletesItsExactOperationOnTheFirstClick()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.BLUR);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), 4, 2);
        session.Editor.SetAnimationTransform(layerId, target, 0, operation);
        var settings = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
        var popup = Assert.IsType<AnimationOptionsPopupView>(Assert.IsType<Flyout>(settings.Flyout).Content);
        var model = Assert.IsType<AnimationOptionsPopupViewModel>(popup.DataContext);
        UiTestActions.Find<NumericDraftInput>(context.Window, "OperationValueInput").RawText = "6";
        var delete = popup.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.DeleteOperationCommand));
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(delete.Command);
        UiTestActions.Click(context.Window, delete);
        await command.ExecutionTask!;
        Assert.DoesNotContain(session.SelectedLayer!.Tracks, track => track.Target == target);
        Assert.Single(session.DocumentSnapshot.Subtitles);
    }
}
