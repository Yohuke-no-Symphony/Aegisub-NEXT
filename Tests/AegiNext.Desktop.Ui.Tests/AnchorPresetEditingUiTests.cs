using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Views;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AnchorPresetEditingUiTests
{
    [AvaloniaFact]
    public async Task FirstWindowConstructionLanguageRoundTripsAndReopeningSettingsResolveAllPresetLabels()
    {
        for (var opening = 0; opening < 2; opening++)
        {
            await using var context = new MainWindowTestContext();
            await context.OpenMediaAsync();
            await PrepareSubtitleAsync(context);
            var window = context.Window;
            window.Layouts.Activate(WorkbenchPanelIds.STYLES);
            await context.Session.RequestSettingsAsync(SettingsPage.STYLES);
            Dispatcher.UIThread.RunJobs();
            var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            try
            {
                foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
                {
                    context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
                    Dispatcher.UIThread.RunJobs();
                    using var mainFrame = window.CaptureRenderedFrame();
                    using var settingsFrame = settings.CaptureRenderedFrame();
                    Assert.NotNull(mainFrame);
                    Assert.NotNull(settingsFrame);
                    AssertPresetLabels(window);
                    AssertPresetLabels(settings);
                }
            }
            finally
            {
                settings.Close();
            }

            await context.Session.RequestSettingsAsync(SettingsPage.STYLES);
            Dispatcher.UIThread.RunJobs();
            settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            try
            {
                AssertPresetLabels(settings);
            }
            finally
            {
                settings.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task AllNineButtonsPreserveTransformedGlyphGeometryAndUndoRestoresTheSnapshot()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = await PrepareSubtitleAsync(context);
        var window = context.Window;
        var original = window.DocumentSnapshot;
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        var before = renderer.GetLayerGeometry(original, MediaTime.Zero, layer.Id)!;
        Assert.True(before.HasInk);
        var names = new[]
        {
            "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
            "BottomLeft", "BottomCenter", "BottomRight"
        };
        for (var index = 0; index < names.Length; index++)
        {
            var button = UiTestActions.Find<Button>(window, $"AnchorPreset{names[index]}");
            var label = $"{Localization.Get("Workbench.AnchorPreset")} · {Localization.Get("Workbench." + names[index])}";
            Assert.Equal(label + Environment.NewLine + Localization.Get("Workbench.AnchorPresetHint"), ToolTip.GetTip(button));
            Assert.Equal(label, AutomationProperties.GetName(button));
            ClickPreset(window, names[index]);
            var position = Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position!;
            Assert.Equal(new ScenePoint(index % 3 / 2d, index / 3 / 2d), position.Anchor);
            Assert.Equal(new ScenePoint(0.5, 1), position.Pivot);
            Assert.Same(layer.Transform, Assert.Single(window.DocumentSnapshot.Layers).Transform);
            AssertSameCorners(before, renderer.GetLayerGeometry(window.DocumentSnapshot, MediaTime.Zero, layer.Id)!);

            window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(original, window.DocumentSnapshot);
        }
    }

    [AvaloniaFact]
    public async Task ShiftAndAltClickUseActualPointerModifiersAndRemainOneUndoableEdit()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var layer = await PrepareSubtitleAsync(context);
        var window = context.Window;
        var original = window.DocumentSnapshot;
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.Session.ProjectDirectory));
        var before = renderer.GetLayerGeometry(original, MediaTime.Zero, layer.Id)!;

        ClickPreset(window, "TopRight", RawInputModifiers.Shift);
        var shifted = Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position!;
        Assert.Equal(new ScenePoint(1, 0), shifted.Anchor);
        Assert.Equal(shifted.Anchor, shifted.Pivot);
        AssertSameCorners(before, renderer.GetLayerGeometry(window.DocumentSnapshot, MediaTime.Zero, layer.Id)!);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);

        ClickPreset(window, "MiddleLeft", RawInputModifiers.Shift | RawInputModifiers.Alt);
        var reset = Assert.Single(window.DocumentSnapshot.Subtitles).Style.Position!;
        Assert.Equal(new ScenePoint(0, 0.5), reset.Anchor);
        Assert.Equal(reset.Anchor, reset.Pivot);
        Assert.Equal(new ScenePoint(), reset.Offset);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
    }

    private static async Task<ProjectLayer> PrepareSubtitleAsync(MainWindowTestContext context)
    {
        var window = context.Window;
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var layer = Assert.Single(window.DocumentSnapshot.Layers) with
        {
            Transform = new(X: 7, Y: -3, ScaleX: 1.5, ScaleY: 0.75, Rotation: 43, AnchorX: 2, AnchorY: -4)
        };
        context.Session.Editor.Apply("Anchor geometry fixture", document => document with
        {
            Width = 320, Height = 180,
            Subtitles = [Assert.Single(document.Subtitles) with
            {
                Text = "  ABC\nDEF  ", Style = new()
                {
                    FontFamily = "sans-serif", FontSize = 22, Margins = new(8, 8, 8),
                    StrokeWidth = 0, ShadowColor = SceneColor.Transparent
                }
            }],
            Layers = [layer]
        });
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        window.Layouts.Activate(WorkbenchPanelIds.STYLES);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.Styles.Subtitle.Position.CanSelectPreset);
        Assert.Equal(layer.Id, context.Session.SelectedLayer!.Id);
        return layer;
    }

    private static void ClickPreset(MainWindow window, string name, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var button = UiTestActions.Find<Button>(window, $"AnchorPreset{name}");
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertSameCorners(ProjectLayerGeometry before, ProjectLayerGeometry after)
    {
        Assert.Equal(4, after.WorldCorners.Count);
        for (var index = 0; index < before.WorldCorners.Count; index++)
        {
            Assert.InRange(Math.Abs(before.WorldCorners[index].X - after.WorldCorners[index].X), 0, 0.001f);
            Assert.InRange(Math.Abs(before.WorldCorners[index].Y - after.WorldCorners[index].Y), 0, 0.001f);
        }
    }

    private static void AssertPresetLabels(Window window)
    {
        foreach (var name in new[]
                 {
                     "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
                     "BottomLeft", "BottomCenter", "BottomRight"
                 })
        {
            var button = UiTestActions.Find<Button>(window, $"AnchorPreset{name}");
            var label = $"{Localization.Get("Workbench.AnchorPreset")} · {Localization.Get("Workbench." + name)}";
            Assert.Equal(label + Environment.NewLine + Localization.Get("Workbench.AnchorPresetHint"), ToolTip.GetTip(button));
            Assert.Equal(label, AutomationProperties.GetName(button));
        }
    }
}
