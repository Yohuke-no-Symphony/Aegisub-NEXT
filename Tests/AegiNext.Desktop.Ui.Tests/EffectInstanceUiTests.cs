using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectInstanceUiTests
{
    private const string SOURCE = """
        effect "instance-ui" version 3
        short-clip compress
        var amount = 0.5 suggest ["Full": 1]
        var direction = vec3(1, 2, 3)
        scope clip current
            segment stay flex 1
                at 0 opacity factor(amount)
                at 1 opacity factor(amount)
            end
        end
        """;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropdownKeyboardAcceptsAChoiceAndClosesItsPopup(bool strict)
    {
        var source = SOURCE.Replace("suggest [\"Full\": 1]", strict
            ? "options [\"Half\": 0.5, \"Full\": 1]" : "suggest [\"Half\": 0.5, \"Full\": 1]", StringComparison.Ordinal);
        var drafts = new EffectScriptParameterDraftSet(source);
        var editor = new EffectScriptParameterEditor { Drafts = drafts };
        var window = new Window { Width = 520, Height = 280, Content = editor };
        try
        {
            window.Show();
            Flush(window);
            var combo = Assert.Single(editor.GetVisualDescendants().OfType<ComboBox>());
            Assert.True(combo.Focus());
            UiTestActions.Press(window, Key.F4);
            Assert.True(combo.IsDropDownOpen);
            UiTestActions.Press(window, Key.End);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            Assert.False(combo.IsDropDownOpen);
            Assert.True(drafts.TryCapture(out var values));
            Assert.Equal(1m, values["amount"].Number);
            Assert.Equal("1", drafts.Rows[0].RawText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectingAnInstanceLoadsItsParameterControlsWithoutAnEditAndRealInputCommitsOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context, new(4));
        await context.Session.EffectScripts.Completion;
        UiTestActions.SelectBuiltinPreset(context.Window, "fade-in");
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        await context.ViewModel.EffectPresets.ApplyCommand.ExecutionTask!;
        var added = context.Session.DocumentSnapshot;
        var layer = added.Layers[0];
        var instance = Assert.Single(layer.Effects);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Assert.True(context.ViewModel.Timeline.SelectEffectClip(new(new(layer.Id, new(AnimationProperty.OPACITY), instance.Id))));
        Flush(context.Window);
        Assert.Same(added, context.Session.DocumentSnapshot);
        var editor = UiTestActions.Find<EffectScriptParameterEditor>(context.Window, "InstanceParameterEditor");
        var duration = editor.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "EffectParameter_duration");
        duration.BringIntoView();
        Flush(context.Window);
        Assert.True(duration.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("0.6s");
        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);
        Assert.Equal(new MediaTime(3, 5), context.Session.DocumentSnapshot.Layers[0].Effects[0].Parameters["duration"].Duration);
        Assert.Empty(context.Session.DocumentSnapshot.Layers[0].Tracks);
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles[0].AnimationRanges);
        Assert.False(context.Session.HasProjectDrafts);
        var selector = UiTestActions.Find<ComboBox>(context.Window, "EffectInstancesCombo");
        Assert.Equal(instance.Id, Assert.IsType<EffectInstance>(selector.SelectedItem).Id);
        Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == instance.Name);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(added, context.Session.DocumentSnapshot);
        Assert.Equal(instance.Id, context.ViewModel.Effects.SelectedEffectInstance!.Id);
        Flush(context.Window);
        Assert.Equal(instance.Id, Assert.IsType<EffectInstance>(selector.SelectedItem).Id);
        Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == instance.Name);
    }

    [AvaloniaFact]
    public void RealTextInputRetainsInvalidParameterAndEscapeRestoresOnlyTheFocusedCoordinate()
    {
        var drafts = new EffectScriptParameterDraftSet(SOURCE);
        var editor = new EffectScriptParameterEditor { Drafts = drafts };
        var window = new Window { Width = 520, Height = 280, Content = editor };
        try
        {
            window.Show();
            Flush(window);
            var scalar = editor.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.Name == "EffectParameter_amount");
            Assert.True(scalar.FocusInput());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("1e-");
            Assert.Equal("1e-", drafts.Rows[0].RawText);
            Assert.False(drafts.TryCapture(out _));
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("0.5", drafts.Rows[0].RawText);
            var vector = drafts.Rows[1];
            vector.X.RawText = "7";
            var y = editor.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.Name == "EffectParameter_direction_y");
            Assert.True(y.FocusInput());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("invalid");
            Assert.Equal("invalid", vector.Y.RawText);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("7", vector.X.RawText);
            Assert.Equal("2", vector.Y.RawText);
            Assert.True(drafts.TryCapture(out var values));
            Assert.Equal(new SceneVector3(7, 2, 3), values["direction"].Animation!.Value.Vector3);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("move")]
    [InlineData("start")]
    [InlineData("end")]
    public void EffectBlockMovesOrTrimsIndependentlyAndCommitsOneUndoOnRelease(string mode)
    {
        var offset = new MediaTime(1, 3);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20),
            Start = new(2), End = new(8), AnimationOffset = offset };
        var document = ProjectEditingOperations.AddEffectInstances(new() { Layers = [layer] }, [layer.Id], "Instance", SOURCE);
        var instance = document.Layers[0].Effects[0];
        document = ProjectEditingOperations.SetEffectInstanceTiming(document, layer.Id, instance.Id, offset + new MediaTime(1), offset + new MediaTime(3));
        layer = document.Layers[0];
        var instanceId = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY), instance.Id);
        var project = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = false, IsStepEnabled = false, PixelsPerSecond = 60 };
        timeline.SetDocument(document, null, layer);
        timeline.EffectClipSelected += (_, args) => args.SelectionAccepted = true;
        var commits = 0;
        timeline.EffectInstanceTimingChanged += (_, args) =>
        {
            commits++;
            Assert.Same(document, args.ExpectedDocument);
            project.SetEffectInstanceTiming(args.Id.LayerId, args.Id.InstanceId!.Value, args.Start, args.End);
        };
        var window = new Window { Width = 900, Height = 220, Content = timeline };
        try
        {
            window.Show();
            Flush(window);
            var block = timeline.GetEffectClipRectangle(instanceId)!.Value;
            Assert.Null(timeline.GetEffectClipExpanderRectangle(instanceId));
            var local = new Point(mode == "start" ? block.Left + 2 : mode == "end" ? block.Right - 2 : block.Left + 30, block.Top + 10);
            var origin = timeline.TranslatePoint(local, window)!.Value;
            var destination = origin + new Vector(60, 0);
            window.MouseDown(origin, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(destination);
            Assert.Equal(0, commits);
            Assert.Same(document, project.Snapshot);
            var preview = timeline.GetEffectClipRectangle(instanceId)!.Value;
            Assert.Equal(block.Left + (mode == "end" ? 0 : 60), preview.Left, 6);
            Assert.Equal(block.Right + (mode == "start" ? 0 : 60), preview.Right, 6);
            window.MouseUp(destination, MouseButton.Left);
            Assert.Equal(1, commits);
            var result = project.Snapshot.Layers[0];
            Assert.Equal(layer.Start, result.Start);
            Assert.Equal(layer.End, result.End);
            Assert.Equal(layer.AnimationOffset, result.AnimationOffset);
            Assert.Equal(layer.Tracks, result.Tracks);
            Assert.True(project.Undo());
            Assert.Same(document, project.Snapshot);
            Assert.False(project.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
