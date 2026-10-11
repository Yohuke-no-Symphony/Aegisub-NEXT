using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchLayoutsUiTests
{
    [AvaloniaFact]
    public void InitialWorkspaceMaterializesWithoutTheDeferredPresentationQueue()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window { Width = 1200, Height = 800 };
        var panels = CreatePanels();
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath,
            () => true, () => { }, _ => { });
        DeferredContentScheduling.SetDelay(controller.Host, TimeSpan.FromDays(1));
        owner.Content = controller.Host;
        try
        {
            owner.Show();
            Flush(owner);

            var tools = controller.Host.GetVisualDescendants().OfType<ToolControl>().ToArray();
            Assert.Equal(5, tools.Length);
            Assert.All(tools, tool => Assert.IsType<WorkbenchToolControl>(tool));
            AssertAttached(panels[WorkbenchPanelIds.PREVIEW], owner);
            AssertAttached(panels[WorkbenchPanelIds.DRAWING_TOOLS], owner);
            AssertAttached(panels[WorkbenchPanelIds.TIMELINE], owner);
            AssertAttached(panels[WorkbenchPanelIds.SUBTITLES], owner);
            AssertAttached(panels[WorkbenchPanelIds.STYLES], owner);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RestoredFloatingBoundsSurviveCaptureBeforeTheOwnerIsShown()
    {
        using var environment = new UiTestEnvironment();
        var restored = new WorkspaceLayoutSnapshot
        {
            Main = WorkspaceLayoutPresets.Tabs(1, "timeline", "subtitles", "styles", "effects", "export", "log"),
            HiddenPanelIds = [WorkbenchPanelIds.SUBTITLE_DETAILS, WorkbenchPanelIds.MASKS, WorkbenchPanelIds.ASSETS, WorkbenchPanelIds.DRAWING_TOOLS],
            Floating = [new() { Content = WorkspaceLayoutPresets.Tabs(1, "preview"), X = 90, Y = 100,
                Width = 620, Height = 420, Scaling = 1 }],
            FocusedPanelId = "preview"
        };
        await new WorkspaceLayoutStore(environment.DirectoryPath).SaveAsync(new() { Current = restored });
        var owner = new Window { Width = 1200, Height = 800 };
        var created = new List<Window>();
        using var controller = new WorkbenchLayoutController(owner, CreatePanels(), environment.DirectoryPath,
            () => true, () => { }, created.Add);
        try
        {
            Assert.False(Assert.Single(created).IsVisible);
            var captured = Assert.Single(controller.Capture().Floating);
            Assert.Equal(90, captured.X);
            Assert.Equal(100, captured.Y);
            Assert.Equal(620, captured.Width);
            Assert.Equal(420, captured.Height);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void DockTabPointerPressCancelsBeforeThePreviousEditorLosesFocus()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window { Width = 1200, Height = 800 };
        var panels = CreatePanels();
        var cancellations = 0;
        var cancellationsWhenFocusWasLost = -1;
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath,
            () => true, () => cancellations++, _ => { });
        owner.Content = controller.Host;
        try
        {
            owner.Show();
            Flush(owner);
            var input = Assert.IsType<TextBox>(panels[WorkbenchPanelIds.PREVIEW]);
            input.Focus();
            Flush(owner);
            cancellations = 0;
            input.LostFocus += (_, _) => cancellationsWhenFocusWasLost = cancellations;
            var tab = controller.Host.GetVisualDescendants().OfType<ToolTabStripItem>()
                .First(item => item.DataContext is WorkbenchDockPanel { Id: WorkbenchPanelIds.STYLES });
            var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), owner)!.Value;

            owner.MouseDown(point, MouseButton.Left);
            owner.MouseUp(point, MouseButton.Left);
            Flush(owner);

            Assert.True(cancellations > 0);
            Assert.True(cancellationsWhenFocusWasLost > 0);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwitchingLayoutsReusesEveryViewAndAdapter()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window { Width = 1200, Height = 800 };
        var panels = CreatePanels();
        var commits = 0;
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath,
            () => { commits++; return true; }, () => { }, _ => { });
        var adapters = controller.PanelAdapters.ToDictionary(pair => pair.Key, pair => pair.Value);
        owner.Content = controller.Host;
        try
        {
            owner.Show();
            Flush(owner);
            Assert.False(controller.IsModified);
            AssertAttached(panels[WorkbenchPanelIds.PREVIEW], owner);
            AssertAttached(panels[WorkbenchPanelIds.TIMELINE], owner);
            AssertAttached(panels[WorkbenchPanelIds.SUBTITLES], owner);
            AssertAttached(panels[WorkbenchPanelIds.STYLES], owner);
            Assert.True(await controller.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
            Flush(owner);
            Assert.True(await controller.ApplyPresetAsync(WorkspaceLayoutPresets.EFFECTS));
            Flush(owner);
            Assert.True(await controller.RestoreDefaultAsync());
            Flush(owner);

            Assert.Equal(3, commits);
            foreach (var id in WorkbenchPanelIds.All)
            {
                Assert.Same(panels[id], controller.PanelAdapters[id].View);
                Assert.Same(adapters[id], controller.PanelAdapters[id]);
                Assert.Equal(id != WorkbenchPanelIds.SUBTITLE_DETAILS, controller.IsVisible(id));
            }
            Assert.False(controller.IsModified);
            foreach (var id in WorkbenchPanelIds.All)
            {
                controller.Activate(id);
                Flush(owner);
                AssertAttached(panels[id], owner);
            }
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task InvalidDraftBlocksPresetSwitchWithoutLosingInputOrCommitting()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var panels = CreatePanels();
        var input = Assert.IsType<TextBox>(panels[WorkbenchPanelIds.SUBTITLES]);
        input.Text = "invalid pending draft";
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath, () => false, () => { }, _ => { });
        var before = WorkspaceLayoutStore.Fingerprint(controller.Capture());
        Assert.False(await controller.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));

        Assert.Equal(WorkspaceLayoutPresets.STANDARD, controller.CurrentPresetId);
        Assert.Equal(before, WorkspaceLayoutStore.Fingerprint(controller.Capture()));
        Assert.Equal("invalid pending draft", input.Text);
        Assert.NotNull(controller.LastError);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task HidingAndReopeningPreservesDraftWithoutCallingCommitBoundary()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window { Width = 1200, Height = 800 };
        var panels = CreatePanels();
        var commits = 0;
        var cancellations = 0;
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath,
            () => { commits++; return true; }, () => cancellations++, _ => { });
        owner.Content = controller.Host;
        try
        {
            owner.Show();
            Flush(owner);
            var input = Assert.IsType<TextBox>(panels[WorkbenchPanelIds.SUBTITLES]);
            input.Text = "keep me";
            controller.Hide(WorkbenchPanelIds.SUBTITLES);
            Flush(owner);
            Assert.False(controller.IsVisible(WorkbenchPanelIds.SUBTITLES));
            controller.Show(WorkbenchPanelIds.SUBTITLES);
            Flush(owner);

            Assert.True(controller.IsVisible(WorkbenchPanelIds.SUBTITLES));
            Assert.Equal("keep me", input.Text);
            Assert.Equal(0, commits);
            Assert.True(cancellations > 0);
            await controller.FlushAsync();
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task PersonalPresetCrudPreservesBuiltInsAndCurrentLayoutAutoMemory()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = new WorkbenchLayoutController(owner, CreatePanels(), environment.DirectoryPath, () => true, () => { }, _ => { });
        Assert.False(await controller.SaveAsync());
        var id = await controller.SaveAsAsync("Personal");
        Assert.NotNull(id);
        controller.Hide(WorkbenchPanelIds.EXPORT);
        await controller.FlushAsync();
        var persisted = new WorkspaceLayoutStore(environment.DirectoryPath).Load();
        Assert.Contains(WorkbenchPanelIds.EXPORT, persisted.Current.HiddenPanelIds);
        Assert.DoesNotContain(WorkbenchPanelIds.EXPORT, Assert.Single(persisted.Presets).Layout.HiddenPanelIds);
        Assert.True(await controller.SaveAsync());
        Assert.Contains(WorkbenchPanelIds.EXPORT, Assert.Single(new WorkspaceLayoutStore(environment.DirectoryPath).Load().Presets).Layout.HiddenPanelIds);
        Assert.True(await controller.RenameAsync(id, "Renamed"));
        Assert.Null(await controller.SaveAsAsync("renamed"));
        Assert.False(await controller.DeleteAsync(WorkspaceLayoutPresets.STANDARD));
        Assert.True(await controller.DeleteAsync(id));
        Assert.Equal(4, controller.Presets.Count);
        Assert.Equal(WorkspaceLayoutPresets.STANDARD, controller.CurrentPresetId);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ClosingAFloatingWindowHidesItsPanelAndKeepsTheSessionHostAlive()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window { Width = 1200, Height = 800 };
        var created = new List<Window>();
        var panels = CreatePanels();
        using var controller = new WorkbenchLayoutController(owner, panels, environment.DirectoryPath, () => true, () => { }, created.Add);
        owner.Content = controller.Host;
        try
        {
            owner.Show();
            Flush(owner);
            controller.Float(WorkbenchPanelIds.PREVIEW);
            Flush(owner);
            var floating = Assert.Single(created);
            Assert.True(floating.IsVisible);
            Assert.Same(panels[WorkbenchPanelIds.PREVIEW], controller.PanelAdapters[WorkbenchPanelIds.PREVIEW].View);
            floating.Close();
            Flush(owner);

            Assert.True(owner.IsVisible);
            Assert.False(controller.IsVisible(WorkbenchPanelIds.PREVIEW));
            Assert.Empty(controller.FloatingWindows);
            controller.Show(WorkbenchPanelIds.PREVIEW);
            Flush(owner);
            Assert.True(controller.IsVisible(WorkbenchPanelIds.PREVIEW));
            Assert.Same(panels[WorkbenchPanelIds.PREVIEW], controller.PanelAdapters[WorkbenchPanelIds.PREVIEW].View);
            await controller.FlushAsync();
        }
        finally
        {
            foreach (var floating in created)
            {
                floating.Close();
            }
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task LanguageRefreshLocalizesBuiltInsAndPreservesPersonalNamesAndManagerDrafts()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = new WorkbenchLayoutController(owner, CreatePanels(), environment.DirectoryPath, () => true, () => { }, _ => { });
        var id = await controller.SaveAsAsync("Personal 原文");
        using var manager = new LayoutPresetManagerViewModel(controller);
        manager.Selected = manager.Presets.Single(row => row.Id == id);
        manager.Name = "尚未提交的名字";

        AegiNext.Desktop.I18n.Localization.SetLanguage("zh-CN");

        Assert.Equal("标准", WorkbenchLayoutController.GetPresetName(controller.Presets[0]));
        Assert.Equal("Personal 原文", WorkbenchLayoutController.GetPresetName(controller.Presets.Single(preset => preset.Id == id)));
        Assert.Equal("尚未提交的名字", manager.Name);
        Assert.Equal("视频预览", controller.PanelAdapters[WorkbenchPanelIds.PREVIEW].Title);
        owner.Close();
    }

    private static Dictionary<string, Control> CreatePanels()
    {
        return WorkbenchPanelIds.All.ToDictionary(id => id, _ => (Control)new TextBox { Text = "Initial draft" });
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void AssertAttached(Control view, Window owner)
    {
        Assert.True(view.IsAttachedToVisualTree(), "The actual fixed panel view must be materialized in the dock host.");
        Assert.Same(owner, TopLevel.GetTopLevel(view));
        Assert.NotNull(view.TranslatePoint(new Point(0, 0), owner));
        Assert.True(view.Bounds.Width > 0);
        Assert.True(view.Bounds.Height > 0);
    }
}
