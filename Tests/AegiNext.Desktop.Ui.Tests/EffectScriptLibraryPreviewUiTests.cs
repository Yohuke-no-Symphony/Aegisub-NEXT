using System.Collections.Concurrent;
using System.Reflection;
using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Windowing;
using AegiNext.Media.Playback;
using AegiNext.Rendering.Fonts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectScriptLibraryPreviewUiTests
{
    [AvaloniaFact]
    public async Task BuiltinPreviewParameterInputUpdatesTheRequestWithoutChangingSourceOrSaving()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var builtin = model.Effects.Single(item => item.IsBuiltin && item.Preset.Source == BuiltinEffectScripts.Get("letter-bounce").Source);
        model.SelectEffects(builtin.Id, [builtin.Id]);
        var original = model.Draft;
        var source = model.Source;
        var saves = 0;
        model.SaveRequested += (_, _) => saves++;
        var view = new EffectScriptLibraryView(() => renderer, timeProvider: new ManualPlaybackTimeProvider(),
            debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 900, Height = 420, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            Assert.True(model.IsReadOnly);
            Assert.True(model.HasPreviewParameters);
            var parameters = UiTestActions.Find<EffectScriptParameterEditor>(window, "PreviewParameterEditor");
            var input = parameters.GetVisualDescendants().OfType<TextBox>().Single(field => field.Name == "EffectParameter_duration");
            input.BringIntoView();
            Flush(window);
            Assert.True(input.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("0.4s");
            UiTestActions.Press(window, Key.Enter);

            await PresentAsync(window, view);

            Assert.Equal(new MediaTime(2, 5), renderer.Requests.Last().Input.Parameters["duration"].Duration);
            Assert.Equal(source, renderer.Requests.Last().Input.Source);
            Assert.Equal(source, model.Source);
            Assert.Same(original, model.Draft);
            Assert.False(model.IsDirty);
            Assert.Equal(0, saves);
            Assert.Null(model.PreviewError);
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PreviewUsesSharedControlsAndCurrentUnsavedSourceWithoutSavingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        await model.AddCommand.ExecuteAsync(null);
        model.Source += "# unsaved preview\n";
        var pending = model.Draft;
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        var saves = 0;
        model.SaveRequested += (_, _) => saves++;
        try
        {
            window.Show();
            await PresentAsync(window, view);
            Assert.True(model.IsPreviewPlaying);
            Assert.True(model.PreviewLoop);
            Assert.IsType<PropertyRowControl>(UiTestActions.Find<Control>(window, "EffectPreviewTextRow"));
            Assert.IsType<NumericDraftInput>(UiTestActions.Find<Control>(window, "EffectPreviewDurationInput"));
            Assert.IsType<ToolbarToggleButton>(UiTestActions.Find<Control>(window, "EffectPreviewPlayPauseButton"));
            Assert.IsType<ToolbarToggleButton>(UiTestActions.Find<Control>(window, "EffectPreviewLoopButton"));
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            Assert.True(UiTestActions.Find<PreviewViewportControl>(window, "EffectPreviewViewport").Bounds.Height > 0);
            Assert.Equal(model.Source, renderer.Requests.Last().Input.Source);
            Assert.Equal("YOSYMPH666", renderer.Requests.Last().Input.Options.Text);
            Assert.Equal(0, saves);
            Assert.Equal(pending, model.Draft);
            Assert.True(model.IsDirty);
            Assert.Null(model.Error);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
        Assert.Equal(1, renderer.DisposeCount);
        Assert.True(view.DisposalCompletion.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public async Task RapidSourceEditsAreDebouncedAndFailureStaysInPreviewWithoutStealingFocus()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        await model.AddCommand.ExecuteAsync(null);
        var view = new EffectScriptLibraryView(() => renderer) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        var validations = 0;
        model.ValidationFailed += (_, _) => validations++;
        try
        {
            window.Show();
            await PresentAsync(window, view);
            model.PreviewPlayPauseCommand.Execute(null);
            Flush(window);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            Assert.True(input.Focus());
            var revision = model.PreviewRevision;
            model.Source = "effect first broken";
            model.Source = "effect second broken";
            model.Source = "effect last broken";

            await PresentAsync(window, view);

            Assert.Single(renderer.Requests, request => request.Revision > revision);
            Assert.Equal("effect last broken", renderer.Requests.Last().Input.Source);
            Assert.NotNull(model.PreviewError);
            Assert.Null(model.Error);
            Assert.Equal(0, model.DiagnosticLine);
            Assert.Null(model.ValidationStatus);
            Assert.Equal(0, validations);
            Assert.True(input.IsFocused);
            Assert.Null(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            var duration = UiTestActions.Find<NumericDraftInput>(window, "EffectPreviewDurationInput");
            duration.Text = "7e-";
            UiTestActions.Find<TextBox>(window, "EffectPreviewTextInput").Focus();
            Localization.SetLanguage("zh-CN");
            model.RefreshLanguage();
            Flush(window);
            Assert.Equal("7e-", duration.RawText);
            Assert.Equal("7e-", model.PreviewDuration.RawText);
            Assert.False(model.HasPreview);
            Assert.NotNull(model.PreviewError);
            Assert.Equal(0, validations);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentDurationFormattingKeepsTransportAndRecoveryRendersANewValidInput(bool playing)
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var clock = new ManualPlaybackTimeProvider();
        var view = new EffectScriptLibraryView(() => renderer, timeProvider: clock, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            if (!playing)
            {
                model.PreviewPlayPauseCommand.Execute(null);
            }
            model.PreviewPosition = 0.5;
            await PresentAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source;
            var position = model.PreviewPosition;
            var revision = model.PreviewRevision;
            var duration = UiTestActions.Find<NumericDraftInput>(window, "EffectPreviewDurationInput");

            duration.Text = "1.0";
            Flush(window);

            Assert.Equal(revision, model.PreviewRevision);
            Assert.Equal(position, model.PreviewPosition);
            Assert.Equal(playing, model.IsPreviewPlaying);
            Assert.False(model.IsPreviewCompiling);
            Assert.Same(frame, UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            duration.Text = "7e-";
            Flush(window);
            Assert.False(model.HasPreview);
            duration.Text = "1.00";
            await PresentAsync(window, view);
            Assert.True(model.HasPreview);
            Assert.Null(model.PreviewError);
            Assert.False(model.IsPreviewCompiling);
            Assert.Equal(0, model.PreviewPosition);
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HideAndReattachPauseAtThePresentedPositionWithoutRecreatingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            model.PreviewPlayPauseCommand.Execute(null);
            model.PreviewPosition = 0.5;
            await PresentAsync(window, view);
            var position = model.PreviewPosition;
            var revision = model.PreviewRevision;
            var draft = model.Draft;

            view.IsVisible = false;
            Flush(window);
            Assert.False(model.IsPreviewPlaying);
            Assert.Equal(position, model.PreviewPosition);
            window.Content = null;
            window.Content = view;
            view.IsVisible = true;
            await PresentAsync(window, view);

            Assert.False(model.IsPreviewPlaying);
            Assert.Equal(position, model.PreviewPosition);
            Assert.Equal(revision, model.PreviewRevision);
            Assert.Same(draft, model.Draft);
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LatestPreviewMailboxStaysBoundedAndRejectsQueuedPresentationAfterDisposal()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var drains = new ConcurrentQueue<Action>();
        var model = new EffectScriptLibraryViewModel();
        var view = new EffectScriptLibraryView(() => renderer, drains.Enqueue, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            model.PreviewText = "first";
            model.PreviewText = "latest";
            await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, view.PendingPreviewCount);
            Assert.Single(drains);

            await view.DisposePreviewAsync();
            while (drains.TryDequeue(out var drain))
            {
                drain();
            }

            Assert.Equal(0, view.PendingPreviewCount);
            Assert.Null(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            Assert.Equal(1, renderer.DisposeCount);
            Assert.True(view.DisposalCompletion.IsCompletedSuccessfully);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LocalSpaceConsumesBothEdgesAndRepeatWhileEditorTextKeepsItsSpace()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        await model.AddCommand.ExecuteAsync(null);
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            context.WindowRegistry.Register(window, () => "Effect preview", role: WorkbenchWindowRole.EFFECT_LIBRARY);
            window.Show(context.Window);
            await PresentAsync(window, view);
            var button = UiTestActions.Find<ToolbarToggleButton>(window, "EffectPreviewPlayPauseButton");
            var clicks = 0;
            button.Click += (_, _) => clicks++;
            Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(window);

            Assert.False(model.IsPreviewPlaying);
            Assert.Equal(0, clicks);
            Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            Assert.True(input.Focus());
            input.CaretIndex = input.Text!.Length;
            var source = model.Source;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyTextInput(" ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(window);
            Assert.Equal(source + " ", model.Source);
            Assert.False(model.IsPreviewPlaying);
            Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerScrubbingPresentsIntermediateAndReturnedFramesBeforeRelease(bool playing)
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var clock = new ManualPlaybackTimeProvider();
        var view = new EffectScriptLibraryView(() => renderer, timeProvider: clock, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            if (!playing)
            {
                model.PreviewPlayPauseCommand.Execute(null);
                await PresentAsync(window, view);
            }
            var slider = UiTestActions.Find<Slider>(window, "EffectPreviewPositionSlider");
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
            var start = thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            var end = start + new Vector((slider.Bounds.Width - thumb.Bounds.Width) * 0.6, 0);
            window.MouseDown(start, MouseButton.Left);
            try
            {
                window.MouseMove(end);
                await PresentAsync(window, view);
                Assert.InRange(model.PreviewPosition, 0.5, 0.7);
                Assert.False(model.IsPreviewPlaying);
                Assert.Contains(renderer.Positions, position => (double)position.Numerator / position.Denominator >= 0.5);
                Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
                window.MouseMove(start);
                await PresentAsync(window, view);
                Assert.InRange(model.PreviewPosition, 0, 1d / 24);
                Assert.False(model.IsPreviewPlaying);
                Assert.InRange((double)renderer.Positions.Last().Numerator / renderer.Positions.Last().Denominator, 0, 1d / 24);
            }
            finally
            {
                window.MouseUp(start, MouseButton.Left);
            }
            await PresentAsync(window, view);
            Assert.Equal(playing, model.IsPreviewPlaying);
            Assert.InRange(model.PreviewPosition, 0, 1d / 24);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PersonalStyleFontsRefreshAndMultipleSelectionClearsTheIndependentScene()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"),
            TestContext.Current.CancellationToken);
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.ToImmutableArray());
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Personal style 中文", new(), font);
        model.UpdatePreviewStyles([style]);
        model.SelectedPreviewStyle = style;
        var view = new EffectScriptLibraryView(() => renderer, timeProvider: new ManualPlaybackTimeProvider(),
            debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            Assert.Same(font, renderer.Requests.Last().Input.StylePreset!.Font);
            model.PreviewFonts = SystemFontCatalog.Empty;
            await PresentAsync(window, view);
            Assert.Same(SystemFontCatalog.Empty, renderer.Requests.Last().FontCatalog);
            var updated = style with { Style = style.Style with { FontSize = 96 } };
            model.UpdatePreviewStyles([updated]);
            await PresentAsync(window, view);
            Assert.Equal(96, renderer.Requests.Last().Input.StylePreset!.Style.FontSize);
            Assert.Equal(style.Id, Assert.IsType<SubtitleStylePreset>(
                UiTestActions.Find<ComboBox>(window, "EffectPreviewStyleInput").SelectedItem).Id);
            model.UpdatePreviewStyles([]);
            await PresentAsync(window, view);
            Assert.Null(renderer.Requests.Last().Input.StylePreset!.Font);
            var first = model.Effects[0];
            var second = model.Effects[1];
            model.SelectEffects(first.Id, [first.Id, second.Id]);
            Flush(window);
            Assert.False(model.HasPreview);
            Assert.True(UiTestActions.Find<TextBlock>(window, "EffectPreviewPlaceholder").IsEffectivelyVisible);
            Assert.Null(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            Assert.False(UiTestActions.Find<ToolbarToggleButton>(window, "EffectPreviewPlayPauseButton").IsEffectivelyEnabled);
            model.SelectEffects(second.Id, [second.Id]);
            await PresentAsync(window, view);
            Assert.True(model.IsPreviewPlaying);
            Assert.True(model.PreviewLoop);
            Assert.True(UiTestActions.Find<TextBox>(window, "ScriptTextInput").IsReadOnly);
            Assert.NotNull(UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame").Source);
            Assert.False(model.IsDirty);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PreviewStylePickerPreservesStableSelectionAcrossSnapshotAndLanguageRefreshes()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Personal style", new());
        model.UpdatePreviewStyles([style]);
        model.SelectedPreviewStyle = style;
        model.PreviewPlayPauseCommand.Execute(null);
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        try
        {
            window.Show();
            await PresentAsync(window, view);
            var picker = UiTestActions.Find<ComboBox>(window, "EffectPreviewStyleInput");
            for (var index = 0; index < 20; index++)
            {
                var updated = style with { Name = "Style " + index, Style = style.Style with { FontSize = index % 2 == 0 ? 64 : 80 } };
                model.UpdatePreviewStyles([updated]);
                Flush(window);
                Assert.Same(model.SelectedPreviewStyle, picker.SelectedItem);
                Assert.Equal(style.Id, Assert.IsType<SubtitleStylePreset>(picker.SelectedItem).Id);
                Assert.Equal(updated.Style.FontSize, model.SelectedPreviewStyle!.Style.FontSize);
            }
            await PresentAsync(window, view);
            var revision = model.PreviewRevision;
            Localization.SetLanguage("zh-CN");
            model.RefreshLanguage();
            Flush(window);
            Assert.Equal(revision, model.PreviewRevision);
            Assert.Equal(style.Id, Assert.IsType<SubtitleStylePreset>(picker.SelectedItem).Id);
            Assert.Same(model.SelectedPreviewStyle, picker.SelectedItem);
            model.UpdatePreviewStyles([]);
            Flush(window);
            Assert.Same(model.SelectedPreviewStyle, picker.SelectedItem);
            Assert.NotEqual(style.Id, Assert.IsType<SubtitleStylePreset>(picker.SelectedItem).Id);
            Assert.Equal(Localization.Get("EffectLibrary.EffectPreviewDefaultStyle"), model.SelectedPreviewStyle!.Name);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OwnerCloseRequestedDuringPersonalCloseIsRetriedAfterTheChildFinishes()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var window = new EffectLibraryWindow(new WorkbenchPreferences());
        var confirmed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reasons = new List<WindowCloseReason>();
        var confirmations = 0;
        var ownerClosings = 0;
        window.ViewModel.Effects.SaveDraftAsync = _ => Task.FromResult(true);
        window.ViewModel.Effects.ConfirmLeaveAsync = () =>
        {
            confirmations++;
            confirmationStarted.TrySetResult();
            return confirmed.Task;
        };
        window.Closing += (_, args) => reasons.Add(args.CloseReason);
        owner.Closing += (_, _) => ownerClosings++;
        try
        {
            owner.Show();
            window.Show(owner);
            window.SelectPage(EffectLibraryPage.EFFECTS);
            await window.ViewModel.NavigationCompletion;
            await window.ViewModel.Effects.AddCommand.ExecuteAsync(null);
            window.Close();
            await confirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(window.CloseCompletion.IsCompleted);

            owner.Close();

            Assert.Equal(1, confirmations);
            Assert.Contains(WindowCloseReason.WindowClosing, reasons);
            Assert.Contains(WindowCloseReason.OwnerWindowClosing, reasons);
            Assert.Equal(0, ownerClosings);
            Assert.True(owner.IsVisible);
            confirmed.SetResult(1);
            await window.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await window.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.False(owner.IsVisible);
            Assert.Equal(1, ownerClosings);
        }
        finally
        {
            confirmed.TrySetResult(1);
            window.CloseImmediately();
            await window.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await window.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task DeactivationClearsAnUnreleasedLocalSpaceBeforeTheNextPress()
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        var deactivations = 0;
        window.Deactivated += (_, _) => deactivations++;
        try
        {
            window.Show();
            await PresentAsync(window, view);
            Assert.True(UiTestActions.Find<ToolbarToggleButton>(window, "EffectPreviewPlayPauseButton").Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(model.IsPreviewPlaying);
            var callback = Assert.IsType<Action>(typeof(IWindowBaseImpl)
                .GetProperty("Deactivated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(window.PlatformImpl));
            callback();
            Assert.Equal(1, deactivations);
            window.Activate();
            UiTestActions.Press(window, Key.Space);
            Assert.True(model.IsPreviewPlaying);
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoordinatorMarshalsFirstWorkerFontLoadAndRejectsDeliveryAfterWindowClose(bool closeBeforeDelivery)
    {
        using var environment = new UiTestEnvironment();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fontEventAccess = new ConcurrentQueue<bool>();
        var fonts = new SubtitleFontSelectionService(() =>
        {
            started.TrySetResult();
            return release.Task;
        });
        var initialCatalog = fonts.Catalog;
        var loadedCatalog = new SystemFontCatalog(SkiaSharp.SKFontManager.Default, Array.Empty<string>());
        fonts.Changed += (_, _) => fontEventAccess.Enqueue(Dispatcher.UIThread.CheckAccess());
        var loading = Task.Run(fonts.EnsureLoadedAsync);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath),
            fontSelectionService: fonts);
        using var coordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner, page: EffectLibraryPage.EFFECTS);
            var window = Assert.IsType<EffectLibraryWindow>(coordinator.Window);
            var model = window.ViewModel.Effects;
            Assert.Same(initialCatalog, model.PreviewFonts);
            var updateAccess = new ConcurrentQueue<bool>();
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(model.PreviewRevision))
                {
                    updateAccess.Enqueue(Dispatcher.UIThread.CheckAccess());
                }
            };
            release.SetResult(loadedCatalog);
            Assert.True(loading.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(Assert.Single(fontEventAccess));
            Assert.Empty(updateAccess);
            if (closeBeforeDelivery)
            {
                window.CloseImmediately();
            }
            Dispatcher.UIThread.RunJobs();
            if (closeBeforeDelivery)
            {
                Assert.Same(initialCatalog, model.PreviewFonts);
                Assert.Empty(updateAccess);
                Assert.Null(coordinator.Window);
            }
            else
            {
                Assert.Same(loadedCatalog, model.PreviewFonts);
                Assert.True(Assert.Single(updateAccess));
                window.CloseImmediately();
            }
            await window.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult(loadedCatalog);
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.Dispose();
            await coordinator.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryWindowCloseTracksWorkerDisposalAndClearsThePresenter(bool force)
    {
        using var environment = new UiTestEnvironment();
        var window = new EffectLibraryWindow(new AegiNext.Desktop.Settings.WorkbenchPreferences());
        window.ViewModel.Effects.SaveDraftAsync = _ => Task.FromResult(true);
        try
        {
            window.Show();
            window.SelectPage(EffectLibraryPage.EFFECTS);
            await window.ViewModel.NavigationCompletion;
            var view = UiTestActions.Find<EffectScriptLibraryView>(window, "EffectsView");
            await PresentAsync(window, view);
            var presenter = UiTestActions.Find<VideoFramePresenter>(window, "EffectPreviewFrame");
            Assert.NotNull(presenter.Source);
            if (force)
            {
                window.CloseImmediately();
            }
            else
            {
                window.Close();
            }

            await window.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            await window.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(window.IsVisible);
            Assert.True(view.DisposalCompletion.IsCompletedSuccessfully);
            Assert.Same(view.DisposalCompletion, window.DisposalCompletion);
            Assert.Null(presenter.Source);
            window.ViewModel.Effects.PreviewText = "late preview input";
            Flush(window);
            Assert.Null(presenter.Source);
            Assert.Equal(0, view.PendingPreviewCount);
        }
        finally
        {
            window.CloseImmediately();
            await window.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task PresentAsync(Window window, EffectScriptLibraryView view)
    {
        await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
