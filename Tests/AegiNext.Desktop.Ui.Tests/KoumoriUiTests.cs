using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Koumori;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Koumori;
using AegiNext.Desktop.Workspace;
using AegiNext.Koumori;
using AegiNext.Media.Audio;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KoumoriUiTests
{
    [AvaloniaFact]
    public void SettingsKeepInvalidDistanceDraftAndRestoreSavedValue()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.KOUMORI);
            var input = UiTestActions.Find<NumericDraftInput>(window, "MaximumSnapMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var changes = 0;
            window.KoumoriChanged += (_, _) => changes++;
            Assert.True(box.Focus());
            box.SelectAll();
            window.KeyTextInput("1001");
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            Assert.Equal(0, changes);
            window.SelectPage(SettingsPage.MEDIA);
            Localization.SetLanguage("zh-CN");
            window.SelectPage(SettingsPage.KOUMORI);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("自动打轴", window.ViewModel.PageTitle);
            Assert.Equal("1001", input.RawText);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("250", input.RawText);
            Assert.False(window.ViewModel.HasError);
            window.ViewModel.Koumori.Enabled = true;
            Assert.Equal(1, changes);
            Capture(window, "settings-zh-CN.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TimelineToggleIsGatedAndPreviewWindowLoadsWithVirtualizedRows()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var toggle = UiTestActions.Find<ToolbarToggleButton>(context.Window, "TimelineKoumoriButton");
        Assert.False(toggle.IsEnabled);
        context.Session.ApplicationContext.UpdatePreferences(value => value with { Koumori = value.Koumori with { Enabled = true } });
        Assert.True(toggle.IsEnabled);
        context.ViewModel.Timeline.IsKoumoriSnapEnabled = true;
        Assert.True(context.Session.Preferences.Koumori.SnapEnabled);
        var window = new AutoTimingWindow(context.Session);
        try
        {
            window.Show(context.Window);
            var model = Assert.IsType<AutoTimingViewModel>(window.DataContext);
            for (var index = 0; index < 200; index++)
            {
                model.Rows.Add(new(new(index * 2), new(index * 2 + 1), 0.8f));
            }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(window.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 40);
            Localization.SetLanguage("ja-JP");
            Assert.Equal("自動タイミング", window.Title);
            Assert.False(model.ApplyCommand.CanExecute(null));
            Capture(window, "automatic-timing-ja-JP.png");
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void ModelSourceSelectionPreservesManualDraftAcrossLanguagesAndPageChanges()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.KOUMORI);
            window.UpdateLayout();
            var model = window.ViewModel.Koumori;
            var selector = UiTestActions.Find<ComboBox>(window, "KoumoriModelSourceCombo");
            var manualSettings = UiTestActions.Find<StackPanel>(window, "ManualModelSettings");
            var changes = new List<KoumoriPreferences>();
            window.KoumoriChanged += (_, args) => changes.Add(args.Preferences);
            Assert.Equal(KoumoriModelSource.BUNDLED, Assert.IsType<KoumoriModelSourceChoice>(selector.SelectedItem).Source);
            Assert.False(manualSettings.IsEffectivelyVisible);

            selector.SelectedItem = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.MANUAL);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(manualSettings.IsEffectivelyVisible);
            Assert.True(window.ViewModel.HasError);
            var input = UiTestActions.Find<TextBox>(window, "KoumoriModelDirectoryInput");
            Assert.True(input.Focus());
            window.KeyTextInput("/manual/model draft");
            Assert.Equal("/manual/model draft", model.ModelDirectoryText);
            Assert.Equal(string.Empty, Assert.Single(changes).ModelDirectory);
            selector.SelectedItem = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.BUNDLED);
            Dispatcher.UIThread.RunJobs();
            Assert.False(manualSettings.IsEffectivelyVisible);
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(string.Empty, changes[^1].ModelDirectory);

            window.SelectPage(SettingsPage.MEDIA);
            Localization.SetLanguage("zh-CN");
            window.SelectPage(SettingsPage.KOUMORI);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, changes.Count);
            Assert.Equal("内置", model.SelectedModelSource!.Label);
            Assert.Equal("/manual/model draft", model.ModelDirectoryText);
            selector.SelectedItem = model.ModelSources.Single(choice => choice.Source == KoumoriModelSource.MANUAL);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            UiTestActions.Click(window, "ApplyKoumoriModelDirectoryButton");
            Assert.Equal("/manual/model draft", changes[^1].ModelDirectory);
            Assert.Equal(KoumoriModelSource.MANUAL, changes[^1].ModelSource);
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(4, changes.Count);
            Capture(window, "settings-manual-model-zh-CN.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", WorkbenchTheme.LIGHT, 800, 420)]
    [InlineData("zh-CN", WorkbenchTheme.DARK, 800, 420)]
    [InlineData("ja-JP", WorkbenchTheme.DARK, 800, 420)]
    [InlineData("ja-JP", WorkbenchTheme.LIGHT, 800, 620)]
    [InlineData("en-US", WorkbenchTheme.DARK, 800, 620)]
    public async Task RegisteredPreviewReusesItsTitleBarAndFitsActions(string language, WorkbenchTheme theme,
        int width, int height)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        Localization.SetLanguage(language);
        context.WindowRegistry.UpdatePreferences(context.Session.Preferences with { Theme = theme });
        var window = new AutoTimingWindow(context.Session) { Width = width, Height = height };
        try
        {
            var content = window.Content;
            context.WindowRegistry.RegisterAuxiliary(window);
            context.WindowRegistry.RegisterAuxiliary(window);
            window.Show(context.Window);
            var model = Assert.IsType<AutoTimingViewModel>(window.DataContext);
            for (var index = 0; index < 200; index++)
            {
                model.Rows.Add(new(new(index * 2), new(index * 2 + 1), 0.8f));
            }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(content, window.Content);
            var titleBar = Assert.Single(window.GetLogicalDescendants().OfType<WindowTitleBar>());
            Assert.Equal("AutoTimingTitleBar", titleBar.Name);
            Assert.Equal(window.Title, titleBar.Title);
            Assert.Null(titleBar.MenuContent);
            Assert.InRange(window.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 40);
            var firstRow = window.GetVisualDescendants().OfType<ListBoxItem>().First();
            foreach (var column in new[] { (Header: "StartColumnHeader", Input: "StartTimeInput"), (Header: "EndColumnHeader", Input: "EndTimeInput") })
            {
                var header = UiTestActions.Find<TextBlock>(window, column.Header);
                var input = firstRow.GetVisualDescendants().OfType<TextBox>().Single(value => value.Name == column.Input);
                Assert.Equal(header.TranslatePoint(default, window)!.Value.X, input.TranslatePoint(default, window)!.Value.X, 1);
                Assert.Equal(header.Bounds.Width, input.Bounds.Width, 1);
            }
            AssertActionsShareRow(window, "AnalyzeButton", "SelectAllButton", "SelectNoneButton", "AuditionButton", "ApplyTimingButton");
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "CancelAnalysisButton");
            Assert.Equal(2, window.GetVisualDescendants().OfType<Separator>().Count());
            foreach (var name in new[] { "AnalyzeButton", "SelectAllButton", "SelectNoneButton", "AuditionButton", "ApplyTimingButton" })
            {
                var button = UiTestActions.Find<Button>(window, name);
                Assert.Single(button.GetVisualDescendants().OfType<IconText>(), icon => icon.IsEffectivelyVisible);
            }
            Assert.Equal(Localization.Get("Koumori.Audition"), Assert.IsType<IconText>(UiTestActions.Find<Button>(window, "AuditionButton").Content).Text);
            var results = UiTestActions.Find<Border>(window, "TimingResults");
            var resultsOrigin = results.TranslatePoint(default, window)!.Value;
            Assert.InRange(resultsOrigin.Y, titleBar.Bounds.Height, window.ClientSize.Height);
            Assert.True(results.Bounds.Height > 80);
            Assert.Equal(24, window.ClientSize.Height - resultsOrigin.Y - results.Bounds.Height, 1);
            Assert.False(UiTestActions.Find<TextBlock>(window, "AnalysisError").IsEffectivelyVisible);

            Localization.SetLanguage(language == "zh-CN" ? "en-US" : "zh-CN");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(Localization.Get("Koumori.Title"), window.Title);
            Assert.Equal(window.Title, titleBar.Title);
            AssertActionsShareRow(window, "AnalyzeButton", "SelectAllButton", "SelectNoneButton", "AuditionButton", "ApplyTimingButton");
            Capture(window, $"automatic-timing-{theme}-{width}.png");
        }
        finally
        {
            window.Close();
            Assert.DoesNotContain(window, context.WindowRegistry.Windows);
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public async Task AnalyzeButtonCancelsAndResetsThroughTheSameControl()
    {
        await using var context = new MainWindowTestContext((_, _, position, _) =>
            Task.FromResult(new AudioPlaybackSession(new UiAuditionAudioSource(), new UiAuditionAudioOutput(), position)));
        await context.Session.ApplicationContext.Initialization;
        context.Session.ApplicationContext.UpdatePreferences(value => value with { Koumori = value.Koumori with { Enabled = true } });
        await context.OpenMediaAsync();
        Assert.True(context.Session.CanAnalyzeKoumori);
        var tasks = context.Session.ApplicationContext.Tasks;
        tasks.MaximumConcurrentTasks = 1;
        var gate = new TaskCenterTestTask("Hold automatic timing analysis");
        var pending = tasks.Submit(gate);
        var window = new AutoTimingWindow(context.Session) { Height = 420 };
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            window.Show(context.Window);
            var model = Assert.IsType<AutoTimingViewModel>(window.DataContext);
            var button = UiTestActions.Find<Button>(window, "AnalyzeButton");
            var content = Assert.IsType<IconText>(button.Content);
            model.Rows.Add(new(new(1), new(2), 0.8f));
            UiTestActions.Click(window, "SelectNoneButton");
            Assert.All(model.Rows, row => Assert.False(row.IsSelected));
            UiTestActions.Click(window, "SelectAllButton");
            Assert.All(model.Rows, row => Assert.True(row.IsSelected));
            Assert.Same(model.RunCommand, button.Command);
            Assert.Equal(Localization.Get("Koumori.Run"), content.Text);

            UiTestActions.Click(window, "AnalyzeButton");
            var analysis = Assert.IsAssignableFrom<Task>(model.RunCommand.ExecutionTask);
            Assert.True(model.IsBusy);
            Assert.True(button.IsEnabled);
            Assert.Same(model.CancelCommand, button.Command);
            Assert.Equal("Cancel", content.IconKey);
            Assert.True(UiTestActions.Find<ProgressBar>(window, "AnalysisProgress").IsEffectivelyVisible);
            foreach (var name in new[] { "SelectAllButton", "SelectNoneButton", "AuditionButton", "ApplyTimingButton" })
            {
                Assert.False(UiTestActions.Find<Button>(window, name).IsEffectivelyEnabled);
            }
            foreach (var language in new[] { "zh-CN", "ja-JP", "en-US" })
            {
                Localization.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(Localization.Get("Koumori.Cancel"), content.Text);
                Assert.Equal(content.Text, Avalonia.Automation.AutomationProperties.GetName(button));
                AssertActionsShareRow(window, "AnalyzeButton", "SelectAllButton", "SelectNoneButton", "AuditionButton", "ApplyTimingButton");
            }

            UiTestActions.Click(window, "AnalyzeButton");
            Assert.True(model.RunCommand.IsCancellationRequested);
            gate.Finish.TrySetResult();
            gate.Cleanup.TrySetResult();
            await pending.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await analysis.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsBusy);
            Assert.False(model.HasError);
            Assert.Same(model.RunCommand, button.Command);
            Assert.Equal(Localization.Get("Koumori.Run"), content.Text);
            Assert.Equal("AUTO_TIMING", content.IconKey);
            Assert.True(button.IsEnabled);
            Assert.False(UiTestActions.Find<ProgressBar>(window, "AnalysisProgress").IsEffectivelyVisible);
            Assert.Same(button, UiTestActions.Find<Button>(window, "AnalyzeButton"));
        }
        finally
        {
            gate.Finish.TrySetResult();
            gate.Cleanup.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnalysisFailureShowsFeedbackAboveResultsAndRestoresTheAnalyzeButton()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var window = new AutoTimingWindow(context.Session);
        try
        {
            window.Show(context.Window);
            var model = Assert.IsType<AutoTimingViewModel>(window.DataContext);
            UiTestActions.Click(window, "AnalyzeButton");
            await Assert.IsAssignableFrom<Task>(model.RunCommand.ExecutionTask);
            var error = UiTestActions.Find<TextBlock>(window, "AnalysisError");
            var results = UiTestActions.Find<Border>(window, "TimingResults");
            Assert.Equal(Localization.Get("Koumori.Unavailable"), error.Text);
            Assert.True(error.IsEffectivelyVisible);
            Assert.True(error.TranslatePoint(default, window)!.Value.Y + error.Bounds.Height <= results.TranslatePoint(default, window)!.Value.Y);
            var button = UiTestActions.Find<Button>(window, "AnalyzeButton");
            Assert.True(button.IsEnabled);
            Assert.Same(model.RunCommand, button.Command);
            Assert.Equal(Localization.Get("Koumori.Run"), Assert.IsType<IconText>(button.Content).Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, true, 1200)]
    [InlineData(true, true, 1170)]
    [InlineData(false, false, 1170)]
    public void DragFreezesModelCandidatesAndHonorsAltAndGlobalEnable(bool alt, bool enabled, int expectedStart)
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2) };
        var layer = new ProjectLayer { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 100, IsSnapEnabled = false, IsKoumoriAvailable = enabled, IsKoumoriSnapEnabled = true,
            KoumoriBoundaries = [new(19200, KoumoriBoundaryKind.Start, 0.9f, 0)]
        };
        timeline.SetDocument(document, cue.Id, layer);
        var start = MediaTime.Zero;
        timeline.TimingChanged += (_, args) => start = args.Start;
        var window = new Window { Width = 1100, Height = 240, Content = timeline };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rectangle = timeline.GetClipRectangle(layer.Id)!.Value;
            var origin = new Point(rectangle.Left + 1, rectangle.Center.Y);
            var modifiers = alt ? RawInputModifiers.Alt : RawInputModifiers.None;
            window.MouseDown(origin, MouseButton.Left, modifiers);
            Assert.True(timeline.HasActiveDrag);
            timeline.KoumoriBoundaries = [new(20800, KoumoriBoundaryKind.Start, 0.99f, 0)];
            var target = origin + new Vector(17, 0);
            window.MouseMove(target, modifiers);
            window.MouseUp(target, MouseButton.Left, modifiers);
            Assert.Equal(new MediaTime(expectedStart, 1000), start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, false, RawInputModifiers.Shift, 1200)]
    [InlineData(true, true, RawInputModifiers.Shift, 1170)]
    [InlineData(true, true, RawInputModifiers.Alt, 1170)]
    [InlineData(false, false, RawInputModifiers.Shift, 1170)]
    public void ClassicTimingReversesOnlyAvailableModelSnapAndPreservesToggle(bool available, bool enabled,
        RawInputModifiers modifiers, int expectedStart)
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2) };
        var layer = new ProjectLayer { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 100, IsClassicTimingEnabled = true, IsSnapEnabled = false,
            IsKoumoriAvailable = available, IsKoumoriSnapEnabled = enabled,
            KoumoriBoundaries = [new(19200, KoumoriBoundaryKind.Start, 0.9f, 0)]
        };
        timeline.SetDocument(document, cue.Id, layer);
        var start = MediaTime.Zero;
        timeline.ClassicTimingRequested += (_, args) => start = args.Time;
        var window = new Window { Width = 1100, Height = 240, Content = timeline };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rectangle = timeline.GetClipRectangle(layer.Id)!.Value;
            var point = new Point(timeline.HeaderWidth + 117, rectangle.Center.Y);
            window.MouseDown(point, MouseButton.Left, modifiers);
            window.MouseUp(point, MouseButton.Left, modifiers);
            Assert.Equal(new MediaTime(expectedStart, 1000), start);
            Assert.Equal(enabled, timeline.IsKoumoriSnapEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CandidatesThatArriveDuringAnEmptyGestureApplyToTheNextGesture()
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2) };
        var layer = new ProjectLayer { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        using var timeline = new SubtitleTimelineControl();
        timeline.PixelsPerSecond = 100;
        timeline.IsSnapEnabled = false;
        timeline.IsKoumoriAvailable = true;
        timeline.IsKoumoriSnapEnabled = true;
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        var starts = new List<MediaTime>();
        timeline.TimingChanged += (_, args) => starts.Add(args.Start);
        var window = new Window { Width = 1100, Height = 240, Content = timeline };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rectangle = timeline.GetClipRectangle(layer.Id)!.Value;
            var origin = new Point(rectangle.Left + 1, rectangle.Center.Y);
            var target = origin + new Vector(17, 0);
            window.MouseDown(origin, MouseButton.Left);
            timeline.KoumoriBoundaries = [new(19200, KoumoriBoundaryKind.Start, 0.9f, 0)];
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Left);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal([new(1170, 1000), new(1200, 1000)], starts);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SmartSnapTooltipStaysStableAcrossAnalysisStatesAndLanguageChanges()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var model = new TimelinePanelViewModel(context.Session);
        using var panel = new TimelinePanelView(model, context.Session);
        var window = new Window { Width = 900, Height = 300, Content = panel };
        try
        {
            window.Show();
            window.UpdateLayout();
            var toggle = UiTestActions.Find<ToolbarToggleButton>(window, "TimelineKoumoriButton");
            foreach (var language in new[] { "en-US", "zh-CN", "ja-JP" })
            {
                Localization.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                foreach (var state in new[]
                         {
                             (State: KoumoriSnapState.Idle, Starts: false, Ends: false),
                             (State: KoumoriSnapState.Analyzing, Starts: false, Ends: false),
                             (State: KoumoriSnapState.Ready, Starts: true, Ends: false),
                             (State: KoumoriSnapState.Ready, Starts: false, Ends: true),
                             (State: KoumoriSnapState.Ready, Starts: true, Ends: true),
                             (State: KoumoriSnapState.ReadyEmpty, Starts: false, Ends: false),
                             (State: KoumoriSnapState.Failed, Starts: false, Ends: false)
                         })
                {
                    model.SetKoumoriSnapStatus(state.State, state.Starts, state.Ends, "controlled failure");
                    Assert.Equal(state.State, model.KoumoriSnapState);
                    Assert.Equal(state.Starts, model.HasKoumoriStartCandidates);
                    Assert.Equal(state.Ends, model.HasKoumoriEndCandidates);
                    Assert.Equal(Localization.Get("Koumori.Snap"), ToolTip.GetTip(toggle));
                    Assert.Equal(Localization.Get("Koumori.Snap"), Avalonia.Automation.AutomationProperties.GetName(toggle));
                    if (state.State == KoumoriSnapState.Failed)
                    {
                        Assert.Contains("controlled failure", model.KoumoriStatus, StringComparison.Ordinal);
                    }
                }
            }
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("智能吸附", ToolTip.GetTip(toggle));
            Assert.Equal(KoumoriSnapState.Failed, model.KoumoriSnapState);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertActionsShareRow(Window window, params string[] names)
    {
        var buttons = names.Select(name => UiTestActions.Find<Button>(window, name)).ToArray();
        var first = buttons[0];
        var firstOrigin = first.TranslatePoint(default, window)!.Value;
        var centerY = firstOrigin.Y + first.Bounds.Height / 2;
        var previousRight = 0d;
        foreach (var button in buttons)
        {
            var origin = button.TranslatePoint(default, window)!.Value;
            Assert.True(button.Bounds.Width > 0);
            Assert.Equal(first.Bounds.Height, button.Bounds.Height, 1);
            Assert.Equal(centerY, origin.Y + button.Bounds.Height / 2, 1);
            Assert.InRange(origin.X, previousRight, window.ClientSize.Width - button.Bounds.Width);
            Assert.InRange(origin.Y, 0, window.ClientSize.Height - button.Bounds.Height);
            previousRight = origin.X + button.Bounds.Width;
        }
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_KOUMORI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

}
