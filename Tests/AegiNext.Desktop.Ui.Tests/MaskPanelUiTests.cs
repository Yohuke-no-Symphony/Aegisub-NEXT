using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskPanelUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExistingMaskReplacesCreationToolsWithAnEditingToggleWithoutWritingTheProject(bool vectorMask, bool animatedNode)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var mask = CreateMask(vectorMask);
        session.Editor.SetClipMask(session.SelectedLayer!.Id, mask);
        if (animatedNode)
        {
            var node = Assert.IsType<VectorClipMask>(mask).Contours[0].Nodes[0];
            session.Editor.SetKeyframe(session.SelectedLayer!.Id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), new(new(0), node.Position));
        }
        session.SelectCue(cueId);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
        var original = session.DocumentSnapshot;
        var rectangle = UiTestActions.Find<ToolbarToggleButton>(context.Window, "RectangleMaskButton");
        var vector = UiTestActions.Find<ToolbarToggleButton>(context.Window, "VectorMaskButton");
        Assert.False(rectangle.IsEffectivelyEnabled);
        Assert.False(vector.IsEffectivelyEnabled);
        Assert.False(rectangle.IsEffectivelyVisible);
        Assert.False(vector.IsEffectivelyVisible);
        Assert.False(rectangle.Command!.CanExecute(null));
        Assert.False(vector.Command!.CanExecute(null));
        var edit = UiTestActions.Find<ToolbarToggleButton>(context.Window, "EditMaskButton");
        Assert.True(edit.IsEffectivelyVisible);
        Assert.True(edit.IsEffectivelyEnabled);
        Assert.False(edit.IsChecked);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        UiTestActions.Click(context.Window, "EditMaskButton");
        Assert.True(edit.IsChecked);
        Assert.Equal(vectorMask ? CanvasEditMode.MASK_VECTOR : CanvasEditMode.MASK_RECTANGLE, session.SceneEditing.Mode);
        Assert.Same(original, session.DocumentSnapshot);
        UiTestActions.Click(context.Window, "EditMaskButton");
        Assert.False(edit.IsChecked);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(original, session.DocumentSnapshot);
        UiTestActions.Click(context.Window, "EditMaskButton");
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(edit.IsChecked);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(original, session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaskToolsFollowCreationClearUndoRedoAndSubtitleSelection(bool vectorMask)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var maskedCueId = await UiTestActions.CreateSubtitleAsync(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
        var session = context.Session;
        var rectangle = UiTestActions.Find<ToolbarToggleButton>(context.Window, "RectangleMaskButton");
        var vector = UiTestActions.Find<ToolbarToggleButton>(context.Window, "VectorMaskButton");
        var edit = UiTestActions.Find<ToolbarToggleButton>(context.Window, "EditMaskButton");
        Assert.True(rectangle.IsEffectivelyVisible && rectangle.IsEffectivelyEnabled);
        Assert.True(vector.IsEffectivelyVisible && vector.IsEffectivelyEnabled);
        Assert.False(edit.IsEffectivelyVisible);
        Assert.False(edit.Command!.CanExecute(null));
        UiTestActions.Click(context.Window, vectorMask ? "VectorMaskButton" : "RectangleMaskButton");
        session.Editor.SetClipMask(session.SelectedLayer!.Id, CreateMask(vectorMask));
        Dispatcher.UIThread.RunJobs();
        Assert.True(edit.IsEffectivelyVisible && edit.IsEffectivelyEnabled);
        Assert.True(edit.IsChecked);
        Assert.False(rectangle.IsEffectivelyVisible || rectangle.IsEffectivelyEnabled);
        Assert.False(vector.IsEffectivelyVisible || vector.IsEffectivelyEnabled);
        var masked = session.DocumentSnapshot;
        UiTestActions.Click(context.Window, "ClearClipMaskButton");
        await Assert.IsType<AsyncRelayCommand>(context.ViewModel.Masks.ClearMaskCommand).ExecutionTask!;
        Assert.Null(session.SelectedLayer!.Mask);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.True(rectangle.IsEffectivelyVisible && rectangle.IsEffectivelyEnabled);
        Assert.True(vector.IsEffectivelyVisible && vector.IsEffectivelyEnabled);
        Assert.False(edit.IsEffectivelyVisible);
        Assert.False(edit.Command.CanExecute(null));
        Assert.True(session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(masked, session.DocumentSnapshot);
        Assert.True(edit.IsEffectivelyVisible && edit.IsEffectivelyEnabled);
        Assert.False(edit.IsChecked);
        Assert.False(rectangle.Command!.CanExecute(null));
        Assert.False(vector.Command!.CanExecute(null));
        Assert.True(session.Editor.Redo());
        Dispatcher.UIThread.RunJobs();
        Assert.False(edit.IsEffectivelyVisible);
        Assert.True(rectangle.IsEffectivelyVisible && rectangle.IsEffectivelyEnabled);
        Assert.True(vector.IsEffectivelyVisible && vector.IsEffectivelyEnabled);
        Assert.True(session.Editor.Undo());
        var unmaskedCueId = session.Editor.AddSubtitle(new(6), new(10), "Unmasked subtitle");
        session.SelectCue(maskedCueId);
        Dispatcher.UIThread.RunJobs();
        var selected = session.DocumentSnapshot;
        UiTestActions.Click(context.Window, "EditMaskButton");
        Assert.True(edit.IsChecked);
        session.SelectCue(unmaskedCueId);
        Dispatcher.UIThread.RunJobs();
        Assert.False(edit.IsEffectivelyVisible);
        Assert.False(edit.IsChecked);
        Assert.True(rectangle.IsEffectivelyVisible && rectangle.IsEffectivelyEnabled);
        Assert.True(vector.IsEffectivelyVisible && vector.IsEffectivelyEnabled);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        session.SelectCue(maskedCueId);
        Dispatcher.UIThread.RunJobs();
        Assert.True(edit.IsEffectivelyVisible && edit.IsEffectivelyEnabled);
        Assert.False(edit.IsChecked);
        Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(selected, session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task InvalidDraftKeepsTheEditingToggleUncheckedAndLanguageSwitchOnlyUpdatesItsLabel()
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        try
        {
            await context.OpenMediaAsync();
            await UiTestActions.CreateSubtitleAsync(context);
            var session = context.Session;
            session.Editor.SetClipMask(session.SelectedLayer!.Id, CreateMask(false));
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
            var original = session.DocumentSnapshot;
            var field = Assert.Single(session.MaskEditing.Fields, field => field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT && field.Component == 0);
            field.Draft.RawText = "unfinished";
            UiTestActions.Click(context.Window, "EditMaskButton");
            var edit = UiTestActions.Find<ToolbarToggleButton>(context.Window, "EditMaskButton");
            Assert.False(edit.IsChecked);
            Assert.Equal(CanvasEditMode.POSITION, session.SceneEditing.Mode);
            foreach (var language in new[] { "en-US", "zh-CN", "ja-JP" })
            {
                Localization.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Localization.Get("Workbench.MaskEdit"), ToolTip.GetTip(edit));
                Assert.Equal(Localization.Get("Workbench.MaskEdit"), Avalonia.Automation.AutomationProperties.GetName(edit));
                Assert.Equal("unfinished", field.Draft.RawText);
                Assert.False(edit.IsChecked);
                Assert.Same(original, session.DocumentSnapshot);
            }
        }
        finally
        {
            foreach (var field in context.Session.MaskEditing.Fields.ToArray())
            {
                context.ViewModel.Masks.RestoreField(field.Key);
            }
            Localization.SetLanguage(originalLanguage);
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task IndependentDockUsesSquareIconToolbarAndVectorCoordinatesWithStableDraftsAcrossFloatingAndReopening(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        try
        {
            Localization.SetLanguage(language);
            context.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await context.OpenMediaAsync();
            await UiTestActions.CreateSubtitleAsync(context);
            var session = context.Session;
            session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var panel = Assert.IsType<MaskPanelView>(context.Window.Panels["masks"]);
            Assert.Same(context.ViewModel.Masks, panel.DataContext);
            Assert.DoesNotContain(panel.GetVisualDescendants(), control => control is Expander);
            var toolbar = UiTestActions.Find<WrapPanel>(context.Window, "MaskToolbar");
            var buttons = toolbar.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.Equal(9, buttons.Length);
            Assert.All(buttons, button =>
            {
                Assert.Equal(button.Bounds.Width, button.Bounds.Height, 6);
                Assert.True(button.Bounds.Width >= 32);
                Assert.IsType<string>(ToolTip.GetTip(button));
                Assert.DoesNotContain(button.GetVisualDescendants(), child => child is TextBlock { Text.Length: > 0 });
            });
            AssertToolbarLayout(toolbar);
            Assert.Equal(Localization.Get("Workbench.MaskEdit"), ToolTip.GetTip(UiTestActions.Find<Button>(context.Window, "EditMaskButton")));
            Assert.IsType<ToolbarToggleButton>(UiTestActions.Find<Button>(context.Window, "InvertClipMaskButton"));
            Assert.False(UiTestActions.Find<Button>(context.Window, "AddMaskContourButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(context.Window, "DeleteMaskContourButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(context.Window, "DeleteMaskNodeButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(context.Window, "SubdivideMaskButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(context.Window, "ClearMaskNodeAnimationButton").IsEffectivelyEnabled);
            Capture(context.Window, $"mask-dock-{language}-{(dark ? "dark" : "light")}.png");
            var vectors = panel.GetVisualDescendants().OfType<VectorDraftInput>().ToArray();
            Assert.Equal(5, vectors.Length);
            var corner = Assert.Single(vectors, vector => vector.DataContext is MaskVectorField field && field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
            var draft = Assert.IsType<MaskVectorField>(corner.DataContext).X.Draft;
            var x = Assert.Single(corner.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "MASK_RECTANGLE_TOP_LEFT.0");
            x.BringIntoView();
            context.Window.UpdateLayout();
            var text = Assert.Single(x.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("invalid 123");
            Dispatcher.UIThread.RunJobs();
            var original = session.DocumentSnapshot;
            context.Window.Layouts.Float("masks");
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
            floating.RequestedThemeVariant = context.Window.RequestedThemeVariant;
            floating.Width = 520;
            floating.Height = 760;
            floating.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AssertToolbarLayout(toolbar);
            Capture(floating, $"mask-floating-wide-{language}-{(dark ? "dark" : "light")}.png");
            floating.Width = 340;
            floating.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AssertToolbarLayout(toolbar);
            Capture(floating, $"mask-floating-narrow-{language}-{(dark ? "dark" : "light")}.png");
            Assert.Same(panel, context.Window.Panels["masks"]);
            Assert.True(panel.IsAttachedToVisualTree());
            corner = Assert.Single(panel.GetVisualDescendants().OfType<VectorDraftInput>(), vector => vector.DataContext is MaskVectorField field && field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
            Assert.Same(draft, Assert.IsType<MaskVectorField>(corner.DataContext).X.Draft);
            Assert.Equal("invalid 123", draft.RawText);
            x = Assert.Single(corner.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "MASK_RECTANGLE_TOP_LEFT.0");
            text = Assert.Single(x.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("invalid 123", x.RawText);
            Assert.Equal("invalid 123", text.Text);
            Assert.Same(original, session.DocumentSnapshot);
            context.Window.Layouts.Hide("masks");
            context.Window.Layouts.Activate("masks");
            Dispatcher.UIThread.RunJobs();
            corner = Assert.Single(panel.GetVisualDescendants().OfType<VectorDraftInput>(), vector => vector.DataContext is MaskVectorField field && field.Target?.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
            Assert.Same(draft, Assert.IsType<MaskVectorField>(corner.DataContext).X.Draft);
            Assert.Equal("invalid 123", draft.RawText);
            x = Assert.Single(corner.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "MASK_RECTANGLE_TOP_LEFT.0");
            text = Assert.Single(x.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("invalid 123", x.RawText);
            Assert.Equal("invalid 123", text.Text);
            Assert.Same(original, session.DocumentSnapshot);
            Assert.Same(context.ViewModel.Masks, panel.DataContext);
            var currentHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(panel));
            Assert.True(text.Focus());
            UiTestActions.Press(currentHost, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("10", x.RawText);
            Assert.Same(original, session.DocumentSnapshot);
            Assert.True(session.TryCommitDrafts(false), DescribeDrafts(context));
            Capture(currentHost, $"mask-floating-clean-{language}-{(dark ? "dark" : "light")}.png");
        }
        catch (Exception error)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(error.ToString());
            TestContext.Current.TestOutputHelper?.WriteLine(DescribeDrafts(context));
            TestContext.Current.TestOutputHelper?.WriteLine(DescribeInputs(Assert.IsType<MaskPanelView>(context.Window.Panels["masks"])));
            throw;
        }
        finally
        {
            foreach (var field in context.Session.MaskEditing.Fields.ToArray())
            {
                context.ViewModel.Masks.RestoreField(field.Key);
            }
            if (!context.Session.TryCommitDrafts(false))
            {
                TestContext.Current.TestOutputHelper?.WriteLine("cleanup: " + DescribeDrafts(context));
            }
            foreach (var host in context.Window.Layouts.FloatingWindows.ToArray())
            {
                host.Close();
            }
            context.Window.Activate();
            Dispatcher.UIThread.RunJobs();
            Localization.SetLanguage(originalLanguage);
        }
    }

    [AvaloniaFact]
    public async Task ToolButtonsHighlightExclusivelyAndGeneralEscapeEndsToolWithoutAProjectTransaction()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
        var original = context.Session.DocumentSnapshot;
        UiTestActions.Click(context.Window, "RectangleMaskButton");
        var rectangle = UiTestActions.Find<ToolbarToggleButton>(context.Window, "RectangleMaskButton");
        var vector = UiTestActions.Find<ToolbarToggleButton>(context.Window, "VectorMaskButton");
        Assert.True(rectangle.IsChecked);
        Assert.False(vector.IsChecked);
        Assert.Equal(CanvasEditMode.MASK_RECTANGLE, context.Session.SceneEditing.Mode);
        Assert.Null(context.Session.SelectedLayer!.Mask);
        UiTestActions.Click(context.Window, "VectorMaskButton");
        Assert.False(rectangle.IsChecked);
        Assert.True(vector.IsChecked);
        Assert.True(vector.Focus());
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(rectangle.IsChecked);
        Assert.False(vector.IsChecked);
        Assert.Equal(CanvasEditMode.POSITION, context.Session.SceneEditing.Mode);
        Assert.Same(original, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReboundGeneralEndEditingWorksFromToolButtonAndCanvasAndClearsCrosshair(bool canvasFocus)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            ShortcutBindings = context.Session.Preferences.ShortcutBindings.Select(binding => binding.Command == WorkbenchCommand.END_TEXT_INPUT
                ? binding with { Gesture = "F6" } : binding).ToImmutableArray()
        });
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
        var original = context.Session.DocumentSnapshot;
        UiTestActions.Click(context.Window, "RectangleMaskButton");
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        Assert.Equal("Cross", canvas.Cursor?.ToString());
        var board = canvas.ProjectRectangle;
        var start = new Point(board.X + board.Width * 0.25, board.Y + board.Height * 0.25);
        var end = new Point(board.X + board.Width * 0.65, board.Y + board.Height * 0.7);
        var windowStart = canvas.TranslatePoint(start, context.Window)!.Value;
        var windowEnd = canvas.TranslatePoint(end, context.Window)!.Value;
        context.Window.MouseDown(windowStart, MouseButton.Left);
        context.Window.MouseUp(windowStart, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Null(context.Session.SelectedLayer!.Mask);
        context.Window.MouseDown(windowStart, MouseButton.Left);
        context.Window.MouseMove(windowEnd);
        Assert.Same(original, context.Session.DocumentSnapshot);
        context.Window.MouseUp(windowEnd, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var committed = context.Session.DocumentSnapshot;
        Assert.IsType<RectangleClipMask>(context.Session.SelectedLayer!.Mask);
        Assert.NotSame(original, committed);
        var edit = UiTestActions.Find<ToolbarToggleButton>(context.Window, "EditMaskButton");
        Assert.True(canvasFocus ? canvas.Focus() : edit.Focus());
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CanvasEditMode.MASK_RECTANGLE, context.Session.SceneEditing.Mode);
        Assert.True(edit.IsChecked);
        UiTestActions.Press(context.Window, Key.F6);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CanvasEditMode.POSITION, context.Session.SceneEditing.Mode);
        Assert.False(edit.IsChecked);
        Assert.Null(canvas.Cursor);
        Assert.Same(committed, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task PointListSelectsOnlyCurrentContoursStableNodeAndVectorInputCommitsOneUndo(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        try
        {
            Localization.SetLanguage(language);
            context.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await context.OpenMediaAsync();
            await UiTestActions.CreateSubtitleAsync(context);
            var session = context.Session;
            var first = new MaskNode { Position = new(10, 20) };
            var second = new MaskNode { Position = new(100, 50) };
            var hidden = new MaskNode { Position = new(200, 200) };
            var contour = new MaskContour { Nodes = [first, second] };
            session.Editor.SetClipMask(session.SelectedLayer!.Id, new VectorClipMask { Contours = [contour, new() { Nodes = [hidden] }] });
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
            UiTestActions.Click(context.Window, "EditMaskButton");
            var list = UiTestActions.Find<ListBox>(context.Window, "MaskPointList");
            Assert.Equal(new[] { first.Id, second.Id }, list.Items.OfType<MaskPointListItem>().Select(point => point.Id));
            list.BringIntoView();
            context.Window.UpdateLayout();
            var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), context.Window)!.Value;
            context.Window.MouseDown(point, MouseButton.Left);
            context.Window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second.Id, session.SceneEditing.MaskNodeId);
            Capture(context.Window, $"mask-bezier-point-list-{language}-{(dark ? "dark" : "light")}.png");
            var panel = Assert.IsType<MaskPanelView>(context.Window.Panels["masks"]);
            var vector = Assert.Single(panel.GetVisualDescendants().OfType<VectorDraftInput>(), control =>
                control.DataContext is MaskVectorField field && field.Target == new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id));
            var input = Assert.Single(vector.GetVisualDescendants().OfType<NumericDraftInput>(), control => control.Name == "MASK_NODE_POSITION.0");
            input.BringIntoView();
            context.Window.UpdateLayout();
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var original = session.DocumentSnapshot;
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("125");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new ScenePoint(125, 50), Assert.IsType<VectorClipMask>(session.PreviewDocument.Layers[0].Mask).Contours[0].Nodes[1].Position);
            Assert.Same(original, session.DocumentSnapshot);
            UiTestActions.Press(context.Window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            var changed = Assert.IsType<VectorClipMask>(session.SelectedLayer!.Mask);
            Assert.Equal(second.Id, changed.Contours[0].Nodes[1].Id);
            Assert.Equal(new ScenePoint(125, 50), changed.Contours[0].Nodes[1].Position);
            Assert.Same(first, changed.Contours[0].Nodes[0]);
            Assert.Same(original.Layers[0].Mask is VectorClipMask originalVector ? originalVector.Contours[1] : null, changed.Contours[1]);
            Assert.True(session.Editor.Undo());
            Assert.Same(original, session.DocumentSnapshot);
        }
        finally
        {
            Localization.SetLanguage(originalLanguage);
        }
    }
    private static ClipMask CreateMask(bool vectorMask)
    {
        if (!vectorMask)
        {
            return new RectangleClipMask
            {
                TopLeft = new(10, 20),
                BottomRight = new(400, 300)
            };
        }
        return new VectorClipMask
        {
            Contours =
            [
                new()
                {
                    Nodes =
                    [
                        new()
                        {
                            Position = new(100, 100)
                        },
                        new()
                        {
                            Position = new(300, 100)
                        },
                        new()
                        {
                            Position = new(200, 300)
                        }
                    ]
                }
            ]
        };
    }

    private static void AssertToolbarLayout(WrapPanel toolbar)
    {
        var groups = toolbar.Children.OfType<StackPanel>().ToArray();
        Assert.Equal(4, groups.Length);
        Assert.Equal(3, toolbar.GetVisualDescendants().OfType<Separator>().Count());
        foreach (var group in groups)
        {
            Assert.Equal(Avalonia.Layout.Orientation.Horizontal, group.Orientation);
            var buttons = group.Children.OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.InRange(buttons.Length, 1, 3);
            var center = buttons[0].Bounds.Center.Y;
            Assert.All(buttons, button => Assert.Equal(center, button.Bounds.Center.Y, 6));
            for (var i = 1; i < buttons.Length; i++)
            {
                Assert.Equal(4, buttons[i].Bounds.X - buttons[i - 1].Bounds.Right, 6);
            }
            foreach (var separator in group.Children.OfType<Separator>())
            {
                Assert.Equal(center, separator.Bounds.Center.Y, 6);
                Assert.Equal(1, separator.Bounds.Width, 6);
                Assert.Equal(24, separator.Bounds.Height, 6);
            }
            Assert.InRange(group.Bounds.Right, 0, toolbar.Bounds.Width + 0.5);
        }
    }

    private static string DescribeInputs(MaskPanelView panel)
    {
        return string.Join("; ", panel.GetVisualDescendants().OfType<VectorDraftInput>().Select(vector =>
            vector.XFieldKey + " XText=" + vector.XText + "/YText=" + vector.YText + " inputs=" +
            string.Join(",", vector.GetVisualDescendants().OfType<NumericDraftInput>().Select(input =>
                input.Name + " raw=" + input.RawText + "/Text=" + input.Text + "/box=" + input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Text))));
    }

    private static string DescribeDrafts(MainWindowTestContext context)
    {
        return "invalid=" + context.ViewModel.InvalidPanelId + "/" + context.ViewModel.InvalidFieldKey +
            "; error=" + context.ViewModel.Masks.ValidationError + "; fields=" +
            string.Join("; ", context.Session.MaskEditing.Fields.Select(field => field.Key + "=" + field.Draft.RawText + " (base=" + field.Original + ")"));
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("Headless capture directory must be absolute.");
        }
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

}
