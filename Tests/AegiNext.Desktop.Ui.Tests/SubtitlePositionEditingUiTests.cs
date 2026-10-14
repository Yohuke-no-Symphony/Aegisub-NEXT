using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitlePositionEditingUiTests
{
    [AvaloniaFact]
    public async Task AutomaticModePreservesPositionEffectsButResetClearsThemWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        await PrepareActualSubtitleAsync(context);
        var window = context.Window;
        var subtitle = Assert.Single(window.DocumentSnapshot.Subtitles);
        context.Session.Editor.UpdateSubtitle(subtitle.Id, line => line with
        {
            Text = "Ajg", Style = line.Style with
            {
                Alignment = AegiNext.Core.Projects.TextAlignment.BOTTOM_CENTER,
                Position = new() { Offset = new(70, -25) }
            }
        });
        var sourceLayer = Assert.Single(window.DocumentSnapshot.Layers);
        var duration = sourceLayer.End - sourceLayer.Start;
        context.Session.Editor.UpdateLayer(subtitle.Id, layer => layer with
        {
            Transform = layer.Transform with { Position = new(20, 10), Scale = new(2, 3), Rotation = 25 },
            Opacity = 0.75,
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), duration),
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(20, 10))]),
                new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(duration, 1)]),
                new(AnimationProperty.OPACITY, [new(new(0), 0.75)])
            ]
        });
        var changed = window.DocumentSnapshot;
        context.Session.Editor.Reset(changed);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        Dispatcher.UIThread.RunJobs();
        var automatic = UiTestActions.Find<RadioButton>(window, "AutomaticPositionMode");
        var custom = UiTestActions.Find<RadioButton>(window, "CustomPositionMode");
        var fields = UiTestActions.Find<StackPanel>(window, "PositionFields");
        Assert.True(custom.IsChecked);
        Assert.True(fields.IsEffectivelyVisible);

        ClickVisibleControl(window, automatic);

        var automaticSnapshot = window.DocumentSnapshot;
        Assert.Null(Assert.Single(automaticSnapshot.Subtitles).Style.Position);
        Assert.Same(Assert.Single(changed.Layers), Assert.Single(automaticSnapshot.Layers));
        Assert.True(automatic.IsChecked);
        Assert.False(custom.IsChecked);
        Assert.False(fields.IsEffectivelyVisible);
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(changed, window.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(custom.IsChecked);
        Assert.True(fields.IsEffectivelyVisible);

        ClickVisibleControl(window, UiTestActions.Find<Button>(window, "AutomaticPositionButton"));
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position);
        var resetLayer = Assert.Single(window.DocumentSnapshot.Layers);
        Assert.Equal(default, resetLayer.Transform.Position);
        Assert.Null(resetLayer.MotionPath);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(resetLayer.Tracks).Property);
        Assert.Equal(new ScenePoint(2, 3), resetLayer.Transform.Scale);
        Assert.Equal(25, resetLayer.Transform.Rotation);
        Assert.Equal(0.75, resetLayer.Opacity);
        Assert.Equal(0, window.ViewModel.Styles.Subtitle.Position.OffsetX.Parse());
        Assert.False(window.ViewModel.Styles.Subtitle.Position.IsExplicit);
        Assert.True(automatic.IsChecked);
        Assert.False(custom.IsChecked);
        Assert.False(fields.IsEffectivelyVisible);
        Assert.Equal(window.DocumentSnapshot.Width / 2m, window.ViewModel.Effects.PositionX);
        var resetSnapshot = window.DocumentSnapshot;
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(changed, window.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(custom.IsChecked);
        Assert.True(context.Session.Editor.Redo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(resetSnapshot, window.DocumentSnapshot);
        Assert.True(automatic.IsChecked);
    }

    [AvaloniaFact]
    public async Task MissingEmbeddedFontKeepsTheLoadedProjectEditableAndRepairRestoresMeasuredPosition()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await PrepareActualSubtitleAsync(context);
        var document = window.DocumentSnapshot;
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fonts/missing.ttf");
        var line = document.Subtitles[0] with
        {
            Text = "ABC",
            Style = document.Subtitles[0].Style with
            {
                FontFamily = "Missing embedded face", FontAssetId = font.Id, Position = null
            }
        };
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document with
        {
            Assets = document.Assets.Add(font), Subtitles = [line]
        }));
        context.Session.ResetSelection();
        context.Session.Editor.Reset(loaded);
        context.Session.SelectLayer(Assert.Single(loaded.Layers).Id, [Assert.Single(loaded.Layers).Id]);
        await context.Session.RunCommandAsync(() => context.Controller.SeekAsync(MediaTime.Zero));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(loaded, window.DocumentSnapshot);
        Assert.Contains("missing.ttf", Assert.IsType<string>(window.ViewModel.Error), StringComparison.Ordinal);
        Assert.Equal(Assert.Single(loaded.Layers).Id, context.Session.SelectedLayer!.Id);
        Assert.False(window.ViewModel.Styles.Subtitle.Position.CanCustomize);
        Assert.False(window.ViewModel.Effects.CanEditPosition);
        Assert.Null(window.ViewModel.Effects.PositionX);
        Assert.Null(window.ViewModel.Effects.PositionY);
        Assert.Equal(string.Empty, window.ViewModel.Styles.Subtitle.Position.OffsetY.RawText);
        Assert.Equal(font.Id, window.DocumentSnapshot.Subtitles[0].Style.FontAssetId);
        window.ViewModel.Effects.RotationText = "15";
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(15, window.DocumentSnapshot.Layers[0].Transform.Rotation);
        Assert.Null(window.ViewModel.Effects.PositionX);
        window.ViewModel.Styles.Subtitle.CommitFont("sans-serif");
        await context.Session.RunCommandAsync(() => context.Controller.SeekAsync(MediaTime.Zero));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ABC", window.DocumentSnapshot.Subtitles[0].Text);
        Assert.Null(window.DocumentSnapshot.Subtitles[0].Style.FontAssetId);
        Assert.True(window.ViewModel.Styles.Subtitle.Position.CanCustomize);
        Assert.True(window.ViewModel.Effects.CanEditPosition);
        Assert.NotNull(window.ViewModel.Effects.PositionX);
        Assert.NotNull(window.ViewModel.Effects.PositionY);
        Assert.Null(window.ViewModel.Error);
    }

    [AvaloniaFact]
    public async Task PositionDraftBindsToThePanelAndAbsoluteEffectPositionCommitsAnUndoableDelta()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await PrepareActualSubtitleAsync(context);
        var original = window.DocumentSnapshot;
        var model = window.ViewModel.Styles.Subtitle.Position;
        var originalX = window.ViewModel.Effects.PositionX!.Value;
        var originalY = window.ViewModel.Effects.PositionY!.Value;
        Assert.True(originalY > 0);
        Assert.Equal(0, Assert.Single(original.Layers).Transform.Y);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var custom = UiTestActions.Find<RadioButton>(window, "CustomPositionMode");
        ClickVisibleControl(window, custom);
        Dispatcher.UIThread.RunJobs();

        Assert.True(model.IsExplicit);
        Assert.NotNull(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position);
        Assert.Equal(originalX, window.ViewModel.Effects.PositionX);
        Assert.Equal(originalY, window.ViewModel.Effects.PositionY);
        var offsetBox = FocusVisibleNumericInput(window, WorkbenchPanelIds.STYLES, "OffsetXInput");
        offsetBox.Text = "24";
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(24, Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position!.Offset.X);
        var baseX = window.ViewModel.Effects.PositionX!.Value;
        var styleSnapshot = window.DocumentSnapshot;
        var targetX = baseX + 30;
        var x = FocusVisibleNumericInput(window, WorkbenchPanelIds.EFFECTS, "PositionXInput");
        x.Text = targetX.ToString(CultureInfo.CurrentCulture);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(30, Assert.Single(window.DocumentSnapshot.Layers).Transform.X, 8);
        Assert.Equal(targetX, window.ViewModel.Effects.PositionX);
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.UNDO);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Assert.Single(styleSnapshot.Subtitles).Style, Assert.Single(window.DocumentSnapshot.Subtitles).Style);
        Assert.Equal(0, Assert.Single(window.DocumentSnapshot.Layers).Transform.X);
        Assert.Equal(baseX, window.ViewModel.Effects.PositionX);
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Same(styleSnapshot, window.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task InvalidPositionRawTextPreventsLayoutSwitchAndKeepsThePositionFieldFocused()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await PrepareActualSubtitleAsync(context);
        window.ViewModel.Styles.Subtitle.Position.IsExplicit = true;
        Assert.True(window.ViewModel.TryCommitDrafts());
        var text = FocusVisibleNumericInput(window, WorkbenchPanelIds.STYLES, "AnchorXInput");
        var input = UiTestActions.Find<NumericDraftInput>(window, "AnchorXInput");
        var validText = input.RawText;
        try
        {
            text.Text = "7e-";
            Dispatcher.UIThread.RunJobs();
            var snapshot = window.DocumentSnapshot;

            Assert.False(await window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(snapshot, window.DocumentSnapshot);
            Assert.Equal("7e-", input.RawText);
            Assert.Equal("styles", window.ViewModel.InvalidPanelId);
            Assert.Equal("AnchorXInput", window.ViewModel.InvalidFieldKey);
            Assert.True(input.IsFocused || text.IsFocused);
        }
        finally
        {
            input.RawText = validText;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.TryCommitDrafts());
        }
    }

    private static async Task PrepareActualSubtitleAsync(MainWindowTestContext context)
    {
        var window = context.Window;
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        context.Session.Editor.Apply("Actual subtitle geometry fixture", document => document with
        {
            Width = 256, Height = 160,
            Subtitles = [Assert.Single(document.Subtitles) with
            {
                Text = "ABC", Style = Assert.Single(document.Subtitles).Style with
                {
                    FontFamily = "sans-serif", FontAssetId = null, Position = null
                }
            }]
        });
        var layer = Assert.Single(window.DocumentSnapshot.Layers);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(layer.Id, context.Session.SelectedLayer!.Id);
        Assert.True(window.ViewModel.Styles.Subtitle.HasCue);
    }

    private static TextBox FocusVisibleNumericInput(MainWindow window, string panelId, string name)
    {
        window.Layouts.Activate(panelId);
        var input = UiTestActions.Find<NumericDraftInput>(window, name);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        ClickVisibleControl(window, text);
        Assert.True(text.IsFocused);
        return text;
    }

    private static void ClickVisibleControl(MainWindow window, Control control)
    {
        Assert.True(control.IsEffectivelyEnabled);
        control.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Position input must be visible in the main window.");
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
