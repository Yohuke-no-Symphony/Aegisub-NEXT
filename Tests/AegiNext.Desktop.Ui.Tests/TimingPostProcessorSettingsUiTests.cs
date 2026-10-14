using AegiNext.Desktop.EffectLibrary;
using System.Collections.Specialized;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.TimingPostProcessor;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimingPostProcessorSettingsUiTests
{
    [AvaloniaFact]
    public void RestoringTheDefaultCheckboxTemplateHeightReproducesThePreviousGlyphClipping()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        ListBox? list = null;
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            window.ViewModel.TimingPostProcessor.UpdateStyles([new(Guid.NewGuid(), "新样式 中文 ABC 123", new())]);
            list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
            Assert.True(list.Resources.Remove("CheckBoxMinHeight"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var checkbox = Assert.Single(list.GetVisualDescendants().OfType<CheckBox>());
            var rectangle = UiTestActions.Find<Control>(checkbox, "NormalRectangle");
            var presenter = UiTestActions.Find<ContentPresenter>(checkbox, "PART_ContentPresenter");
            var origin = rectangle.TranslatePoint(default, checkbox)!.Value;
            var rectangleCenter = origin.Y + rectangle.Bounds.Height / 2;
            var presenterCenter = presenter.TranslatePoint(new(0, presenter.Bounds.Height / 2), checkbox)!.Value.Y;

            Assert.Equal(20, rectangle.Bounds.Width);
            Assert.Equal(20, rectangle.Bounds.Height);
            Assert.True(origin.Y + rectangle.Bounds.Height > checkbox.Bounds.Height);
            Assert.InRange(Math.Abs(rectangleCenter - presenterCenter), 2, 8);
        }
        finally
        {
            if (list is not null)
            {
                list.Resources["CheckBoxMinHeight"] = 24d;
            }

            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CoordinatorOwnedAssociatedStylesCanBeSelectedByPointerAndKeyboardWithoutRebuildingTheList()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        var first = new SubtitleStylePreset(Guid.NewGuid(), "A 已关联 ABC 123", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 111, EndAfterMilliseconds = 333 }
        };
        var second = new SubtitleStylePreset(Guid.NewGuid(), "B 已关联 日本語 456", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 222, EndAfterMilliseconds = 444 }
        };
        var preferenceChanges = 0;
        var candidateChanges = 0;
        var styleRefreshes = 0;
        var dispatcherPulses = 0;
        TimingPostProcessorSettingsViewModel? model = null;
        EventHandler preferenceChanged = (_, _) => preferenceChanges++;
        NotifyCollectionChangedEventHandler stylesChanged = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                styleRefreshes++;
            }
        };
        EventHandler<TimingPostProcessorPreferencesChangedEventArgs> candidateChanged = (_, _) => candidateChanges++;
        try
        {
            await context.Initialization;
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(first));
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(second));
            context.PreferencesChanged += preferenceChanged;
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            model = window.ViewModel.TimingPostProcessor;
            model.Changed += candidateChanged;
            model.Styles.CollectionChanged += stylesChanged;
            var originalRows = model.Styles.ToArray();

            for (var index = 0; index < 4; index++)
            {
                var target = index % 2 == 0 ? first : second;
                var beforePreferences = preferenceChanges;
                var beforeCandidates = candidateChanges;
                var beforeRefreshes = styleRefreshes;
                var checkbox = FindTimingStyleCheckbox(window, target.Id);
                if (index < 2)
                {
                    var point = checkbox.TranslatePoint(new(10, checkbox.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
                else
                {
                    Assert.True(checkbox.Focus(NavigationMethod.Tab));
                    UiTestActions.Press(window, Key.Space);
                }

                Dispatcher.UIThread.RunJobs();
                await Dispatcher.UIThread.InvokeAsync(() => dispatcherPulses++, DispatcherPriority.Background);

                Assert.Equal(target.Id, model.SelectedStyle?.Id);
                Assert.Equal(target.TimingPostProcessor, model.Options);
                Assert.Equal(target.TimingPostProcessor!.LeadInMilliseconds.ToString(CultureInfo.CurrentCulture),
                    UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput").RawText);
                Assert.InRange(candidateChanges - beforeCandidates, 0, 1);
                Assert.InRange(preferenceChanges - beforePreferences, 0, 1);
                Assert.Equal(beforeRefreshes, styleRefreshes);
                Assert.Equal(index + 1, dispatcherPulses);
            }

            await context.Completion;
            Assert.Equal(4, dispatcherPulses);
            Assert.InRange(candidateChanges, 0, 4);
            Assert.InRange(preferenceChanges, 0, 4);
            Assert.Equal(0, styleRefreshes);
            Assert.All(originalRows, row => Assert.Same(row, model.Styles.Single(style => style.Id == row.Id)));
            Assert.False(window.ViewModel.HasError);
        }
        finally
        {
            context.PreferencesChanged -= preferenceChanged;
            if (model is not null)
            {
                model.Changed -= candidateChanged;
                model.Styles.CollectionChanged -= stylesChanged;
            }

            coordinator.Dispose();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", true, 1d)]
    [InlineData("zh-CN", true, 1.5d)]
    [InlineData("zh-CN", true, 2d)]
    [InlineData("en-US", false, 1d)]
    [InlineData("en-US", false, 1.5d)]
    [InlineData("en-US", false, 2d)]
    public async Task CoordinatorOwnedStyleRowsRenderCheckedAndUncheckedAssociatedMixedTextAtDifferentScaling(
        string language, bool dark, double scaling)
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = language });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        SubtitleStylePreset[] presets =
        [
            new(Guid.NewGuid(), "新样式", new()),
            new(Guid.NewGuid(), "nano", new(), TimingPostProcessor: new() { LeadInMilliseconds = 222 }),
            new(Guid.NewGuid(), "z 未勾选关联 中文 ABC 123", new(), TimingPostProcessor: new()),
            new(Guid.NewGuid(), "zz 勾选 English 日本語 012", new())
        ];
        try
        {
            await context.Initialization;
            foreach (var preset in presets)
            {
                await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset));
            }

            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            window.Width = 980;
            window.Height = 720;
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.SetRenderScaling(scaling);
            var model = window.ViewModel.TimingPostProcessor;
            model.Styles.Single(style => style.Id == presets[2].Id).IsSelected = false;
            model.Styles.Single(style => style.Id == presets[3].Id).IsSelected = true;
            model.SelectedStyle = model.Styles.Single(style => style.Id == presets[0].Id);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
            Assert.Equal(scaling, window.RenderScaling);
            foreach (var preset in presets)
            {
                var row = list.GetVisualDescendants().OfType<ListBoxItem>().Single(item =>
                    item.DataContext is TimingStyleChoice choice && choice.Id == preset.Id);
                var checkbox = Assert.Single(row.GetVisualDescendants().OfType<CheckBox>());
                var name = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == preset.Name);
                var marker = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "✓");
                var rectangle = UiTestActions.Find<Control>(checkbox, "NormalRectangle");
                var presenter = UiTestActions.Find<ContentPresenter>(checkbox, "PART_ContentPresenter");
                Assert.Equal(20, rectangle.Bounds.Width);
                Assert.Equal(20, rectangle.Bounds.Height);
                Assert.Equal(model.Styles.Single(style => style.Id == preset.Id).IsSelected, checkbox.IsChecked);
                Assert.Equal(preset.TimingPostProcessor is not null, marker.IsEffectivelyVisible);
                var rectangleOrigin = rectangle.TranslatePoint(default, checkbox)!.Value;
                Assert.InRange(rectangleOrigin.X, 0, checkbox.Bounds.Width);
                Assert.InRange(rectangleOrigin.Y, 0, checkbox.Bounds.Height);
                Assert.InRange(rectangleOrigin.X + rectangle.Bounds.Width, 0, checkbox.Bounds.Width);
                Assert.InRange(rectangleOrigin.Y + rectangle.Bounds.Height, 0, checkbox.Bounds.Height);
                var rectangleRowOrigin = rectangle.TranslatePoint(default, row)!.Value;
                Assert.InRange(rectangleRowOrigin.X, 0, row.Bounds.Width);
                Assert.InRange(rectangleRowOrigin.Y, 0, row.Bounds.Height);
                Assert.InRange(rectangleRowOrigin.X + rectangle.Bounds.Width, 0, row.Bounds.Width);
                Assert.InRange(rectangleRowOrigin.Y + rectangle.Bounds.Height, 0, row.Bounds.Height);
                var textCenter = name.TranslatePoint(new(0, name.Bounds.Height / 2), row)!.Value.Y;
                var rectangleCenter = rectangleRowOrigin.Y + rectangle.Bounds.Height / 2;
                var presenterCenter = presenter.TranslatePoint(new(0, presenter.Bounds.Height / 2), row)!.Value.Y;
                Assert.InRange(Math.Abs(textCenter - rectangleCenter), 0, 1);
                Assert.InRange(Math.Abs(presenterCenter - rectangleCenter), 0, 1);
                if (preset.TimingPostProcessor is not null)
                {
                    var markerCenter = marker.TranslatePoint(new(0, marker.Bounds.Height / 2), row)!.Value.Y;
                    Assert.InRange(Math.Abs(markerCenter - rectangleCenter), 0, 1);
                }
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal((int)Math.Round(window.ClientSize.Width * scaling), frame.PixelSize.Width);
            Assert.Equal((int)Math.Round(window.ClientSize.Height * scaling), frame.PixelSize.Height);
            var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Assert.True(Path.IsPathFullyQualified(directory));
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory,
                    $"timing-styles-{language}-{scaling.ToString("0.0", CultureInfo.InvariantCulture)}x.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            coordinator.Dispose();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public void RefreshingTheSelectedAssociationPreservesValidUnconfirmedInputWithoutASelectionBindingCommit()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Bound 中文 ABC 123", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 500 }
        };
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            var model = window.ViewModel.TimingPostProcessor;
            model.UpdateStyles([original]);
            model.SelectedStyle = Assert.Single(model.Styles);
            var input = UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            box.Focus();
            box.Text = "900";
            var changes = 0;
            model.Changed += (_, _) => changes++;

            model.UpdateStyles([original with
            {
                TimingPostProcessor = original.TimingPostProcessor! with { LeadInMilliseconds = 800 }
            }]);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(original.Id, model.SelectedStyle!.Id);
            var list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
            Assert.Equal(original.Id, Assert.IsType<TimingStyleChoice>(list.SelectedItem).Id);
            Assert.Equal(800, model.SelectedStyle.Options!.LeadInMilliseconds);
            Assert.Equal(500, model.Options.LeadInMilliseconds);
            Assert.Equal("900", model.LeadInMillisecondsText);
            Assert.Equal("900", input.RawText);
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedVisualStyleSavePreservesTheLatestAssociationInsteadOfRestoringCapturedMetadata(bool unlink)
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        using var coordinator = new SettingsWindowCoordinator(context);
        using var libraryCoordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { FontSize = 41 })
        {
            TimingPostProcessor = unlink ? new() { LeadInMilliseconds = 500 } : null
        };
        TimingPostProcessorOptions? updatedOptions = unlink ? null : new() { LeadInMilliseconds = 777 };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await context.Initialization;
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(original));
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            await libraryCoordinator.OpenAsync(owner, page: EffectLibraryPage.STYLES);
            var library = Assert.IsType<EffectLibraryWindow>(libraryCoordinator.Window);
            var capturedVisualDraft = original with { Name = "Updated 中文 ABC 123", Style = original.Style with { FontSize = 48 } };
            var blocked = context.RunStyleOperationAsync(async () =>
            {
                entered.TrySetResult();
                await release.Task;
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var association = context.RunStyleOperationAsync(() => context.StyleLibrary.SetTimingPostProcessorAsync([original.Id], updatedOptions));
            var saveDraft = Assert.IsType<Func<SubtitleStylePreset, Task<bool>>>(library.ViewModel.Styles.SaveDraftAsync);
            var visualSave = saveDraft(capturedVisualDraft);
            Assert.False(association.IsCompleted);
            Assert.False(visualSave.IsCompleted);

            release.TrySetResult();
            await Task.WhenAll(blocked, association, visualSave).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(await visualSave);
            var saved = Assert.Single(context.StyleLibrary.Snapshot.Presets);
            Assert.Equal(updatedOptions, saved.TimingPostProcessor);
            Assert.Equal(capturedVisualDraft.Name, saved.Name);
            Assert.Equal(48, saved.Style.FontSize);
            using var persisted = new EffectLibraryRepository(context.LibraryPaths.EffectLibraryDirectory);
            await persisted.LoadAsync();
            Assert.Equal(saved, Assert.Single(persisted.Snapshot.Styles.Presets));
            Assert.False(window.ViewModel.HasError);
        }
        finally
        {
            release.TrySetResult();
            libraryCoordinator.Dispose();
            coordinator.Close();
            await coordinator.TimingCompletion;
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public void ClickingAStyleCheckboxSelectsItsRowAndReadsTheAssociatedParameters()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        var bound = new SubtitleStylePreset(Guid.NewGuid(), "Bound 中文 ABC 123", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 750, EndAfterMilliseconds = 333 }
        };
        var unbound = new SubtitleStylePreset(Guid.NewGuid(), "Other", new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            var model = window.ViewModel.TimingPostProcessor;
            model.UpdateStyles([bound, unbound]);
            var list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
            var checkbox = list.GetVisualDescendants().OfType<CheckBox>().Single(control =>
                control.DataContext is TimingStyleChoice style && style.Id == bound.Id);
            checkbox.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var point = checkbox.TranslatePoint(new(10, checkbox.Bounds.Height / 2), window)!.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(bound.Id, model.SelectedStyle?.Id);
            Assert.Equal(bound.Id, Assert.IsType<TimingStyleChoice>(list.SelectedItem).Id);
            Assert.Equal(750, model.Options.LeadInMilliseconds);
            Assert.Equal("750", UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput").RawText);
            Assert.Equal("333", UiTestActions.Find<NumericDraftInput>(window, "EndAfterMillisecondsInput").RawText);
            Assert.False(model.Styles.Single(style => style.Id == bound.Id).IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AssociatingTimingPreservesUnconfirmedStyleFieldsAndLaterStyleSaveRetainsTheAssociation()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        using var coordinator = new SettingsWindowCoordinator(context);
        using var libraryCoordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { FontSize = 41 });
        try
        {
            await context.Initialization;
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(preset));
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            await libraryCoordinator.OpenAsync(owner, page: EffectLibraryPage.STYLES);
            var library = Assert.IsType<EffectLibraryWindow>(libraryCoordinator.Window);
            var styleModel = library.ViewModel.Styles;
            styleModel.Name = "Unsaved 自定义 中文 ABC 123";
            styleModel.FontSizeText = "invalid draft";
            var timingModel = window.ViewModel.TimingPostProcessor;
            Assert.Single(timingModel.Styles).IsSelected = true;

            UiTestActions.Click(window, "AssociateTimingPostProcessorButton");
            await coordinator.TimingCompletion;

            Assert.Equal("Unsaved 自定义 中文 ABC 123", styleModel.Name);
            Assert.Equal("invalid draft", styleModel.FontSizeText);
            Assert.Equal(timingModel.Options, Assert.Single(context.StyleLibrary.Snapshot.Presets).TimingPostProcessor);
            library.SelectPage(EffectLibraryPage.STYLES);
            styleModel.FontSizeText = "48";
            UiTestActions.Click(library, "SaveStyleButton");
            await context.Completion;
            var saved = Assert.Single(context.StyleLibrary.Snapshot.Presets);
            Assert.Equal("Unsaved 自定义 中文 ABC 123", saved.Name);
            Assert.Equal(48, saved.Style.FontSize);
            Assert.Equal(timingModel.Options, saved.TimingPostProcessor);
        }
        finally
        {
            libraryCoordinator.Dispose();
            coordinator.Close();
            await coordinator.TimingCompletion;
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomStyleAssociationPersistsWithoutAProjectOrAnyProjectTimingTransaction(bool existingProject)
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US", Volume = 0.375f });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        var session = existingProject ? CreateSession(owner, context, editor) : null;
        var first = new SubtitleStylePreset(Guid.NewGuid(), "Default", new());
        var custom = new SubtitleStylePreset(Guid.NewGuid(), "自定义对白 中文 ABC 123", new() { FontSize = 41 });
        try
        {
            await context.Initialization;
            if (session is not null)
            {
                await session.Styles.Completion;
            }

            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(first));
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(custom));
            var preferences = context.Preferences;
            owner.Show();
            await coordinator.OpenAsync(owner, session, SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            var model = window.ViewModel.TimingPostProcessor;
            Assert.Equal(2, model.Styles.Count);
            Assert.Equal(custom.Name, model.Styles.Single(style => style.Id == custom.Id).Name);
            Assert.All(model.Styles, style => Assert.False(style.IsAssociated));
            model.Styles.Single(style => style.Id == custom.Id).IsSelected = true;

            UiTestActions.Click(window, "AssociateTimingPostProcessorButton");
            await coordinator.TimingCompletion;
            await context.Completion;

            Assert.Equal(model.Options, context.StyleLibrary.Snapshot.Presets.Single(style => style.Id == custom.Id).TimingPostProcessor);
            Assert.Null(context.StyleLibrary.Snapshot.Presets.Single(style => style.Id == first.Id).TimingPostProcessor);
            Assert.True(model.Styles.Single(style => style.Id == custom.Id).IsAssociated);
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.Equal(preferences, context.Preferences);
            Assert.False(File.Exists(Path.Combine(environment.DirectoryPath, "preferences.json")));
            Assert.False(model.IsBusy);
            Assert.False(window.ViewModel.HasError);
            coordinator.Close();
            await coordinator.OpenAsync(owner, session, SettingsPage.TIMING_POST_PROCESSOR);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.True(reopened.ViewModel.TimingPostProcessor.Styles.Single(style => style.Id == custom.Id).IsAssociated);
            using var stored = new EffectLibraryRepository(context.LibraryPaths.EffectLibraryDirectory);
            await stored.LoadAsync();
            Assert.Equal(model.Options, stored.Snapshot.Styles.Presets.Single(style => style.Id == custom.Id).TimingPostProcessor);
        }
        finally
        {
            coordinator.Close();
            await coordinator.TimingCompletion;
            owner.Close();
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task PresetRenameRetainsAssociationByIdAndDeletionRemovesItFromTheSettingsList()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        var custom = new SubtitleStylePreset(Guid.NewGuid(), "Original 自定义 123", new());
        try
        {
            await context.Initialization;
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(custom));
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            var model = window.ViewModel.TimingPostProcessor;
            var row = Assert.Single(model.Styles);
            row.IsSelected = true;
            model.SelectedStyle = row;
            UiTestActions.Click(window, "AssociateTimingPostProcessorButton");
            await coordinator.TimingCompletion;
            var saved = Assert.Single(context.StyleLibrary.Snapshot.Presets);
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(saved with { Name = "Renamed 中文 ABC 123" }));

            Assert.Equal(custom.Id, Assert.Single(model.Styles).Id);
            Assert.True(Assert.Single(model.Styles).IsSelected);
            Assert.True(Assert.Single(model.Styles).IsAssociated);
            Assert.Equal("Renamed 中文 ABC 123", Assert.Single(model.Styles).Name);
            Assert.Equal(custom.Id, model.SelectedStyle!.Id);
            UiTestActions.Click(window, "UnlinkTimingPostProcessorButton");
            await coordinator.TimingCompletion;
            Assert.Null(Assert.Single(context.StyleLibrary.Snapshot.Presets).TimingPostProcessor);
            Assert.False(Assert.Single(model.Styles).IsAssociated);
            await context.RunStyleOperationAsync(() => context.StyleLibrary.RemoveAsync(custom.Id));
            Assert.Empty(model.Styles);
            Assert.False(UiTestActions.Find<Button>(window, "AssociateTimingPostProcessorButton").IsEffectivelyEnabled);
        }
        finally
        {
            coordinator.Close();
            await coordinator.TimingCompletion;
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MillisecondsCommitOnEnterOrBlurAndPersistAcrossSettingsReopen(bool blur)
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new()
        {
            Language = "en-US", Volume = 0.375f, TimelineClassicTimingEnabled = true
        });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            var input = UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = "750";
            Assert.Equal(100, context.Preferences.TimingPostProcessor.Options.LeadInMilliseconds);
            if (blur)
            {
                Assert.True(Assert.IsType<ListBoxItem>(UiTestActions.Find<ListBox>(window, "Navigation").SelectedItem).Focus());
            }
            else
            {
                UiTestActions.Press(window, Key.Enter);
            }

            Dispatcher.UIThread.RunJobs();
            await context.Completion;
            Assert.Equal(750, context.Preferences.TimingPostProcessor.Options.LeadInMilliseconds);
            Assert.Equal(0.375f, context.Preferences.Volume);
            Assert.True(context.Preferences.TimelineClassicTimingEnabled);
            Assert.Equal(context.Preferences, context.PreferencesStore.Load());
            Assert.False(UiTestActions.Find<Button>(window, "AssociateTimingPostProcessorButton").IsEffectivelyEnabled);
            coordinator.Close();
            await coordinator.OpenAsync(owner, page: SettingsPage.TIMING_POST_PROCESSOR);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.Equal("750", reopened.ViewModel.TimingPostProcessor.LeadInMillisecondsText);
            Assert.False(reopened.ViewModel.HasError);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData("LeadInMillisecondsInput", "100")]
    [InlineData("LeadOutMillisecondsInput", "350")]
    [InlineData("MaximumGapMillisecondsInput", "300")]
    [InlineData("MaximumOverlapMillisecondsInput", "50")]
    [InlineData("StartBeforeMillisecondsInput", "200")]
    [InlineData("StartAfterMillisecondsInput", "150")]
    [InlineData("EndBeforeMillisecondsInput", "200")]
    [InlineData("EndAfterMillisecondsInput", "250")]
    public void InvalidInputSurvivesNavigationAndLanguageAndEscapeRestoresThatField(string fieldName, string expected)
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences { Language = "en-US" };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            window.ViewModel.TimingPostProcessor.UpdateStyles([new(Guid.NewGuid(), "Default", new()), new(Guid.NewGuid(), "对白 中文 ABC 123", new())]);
            var changes = 0;
            window.ViewModel.TimingPostProcessor.Changed += (_, _) => changes++;
            var input = UiTestActions.Find<NumericDraftInput>(window, fieldName);
            input.BringIntoView();
            window.UpdateLayout();
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = "120.";
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            window.SelectPage(SettingsPage.APPEARANCE);
            Localization.SetLanguage("zh-CN");
            window.RefreshLanguage();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("120.", input.RawText);
            Assert.Equal("120.", box.Text);
            Assert.True(window.ViewModel.HasError);
            Assert.Equal("时间后续处理器", window.ViewModel.PageTitle);
            Assert.Equal(0, changes);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal(expected, input.RawText);
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ImeConfirmationWaitsForCompositionAndParameterCommitOccursOnce()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            var changes = 0;
            window.ViewModel.TimingPostProcessor.Changed += (_, _) => changes++;
            var input = UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = "750";
            var presenter = Assert.Single(box.GetVisualDescendants().OfType<TextPresenter>());
            presenter.PreeditText = "毫秒候选";

            UiTestActions.Press(window, Key.Enter);
            UiTestActions.Press(window, Key.Escape);

            Assert.Equal(0, changes);
            Assert.Equal("750", input.RawText);
            Assert.Equal(100, window.ViewModel.TimingPostProcessor.Options.LeadInMilliseconds);
            presenter.PreeditText = null;
            UiTestActions.Press(window, Key.Enter);
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(1, changes);
            Assert.Equal(750, window.ViewModel.TimingPostProcessor.Options.LeadInMilliseconds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AssociationUsesCustomPresetIdsAndRejectsInvalidNumbersWithoutVideoOrProjectRequirements()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            var model = window.ViewModel.TimingPostProcessor;
            var first = new SubtitleStylePreset(Guid.NewGuid(), "Default", new());
            var custom = new SubtitleStylePreset(Guid.NewGuid(), "对白 中文 ABC 123", new());
            model.UpdateStyles([first, custom]);
            model.Styles.Single(style => style.Id == custom.Id).IsSelected = true;
            var requests = new List<(TimingPostProcessorOptions Options, IReadOnlySet<Guid> Ids)>();
            model.AssociateRequested += (_, request) => requests.Add((request.Options!, request.StyleIds));
            var input = UiTestActions.Find<NumericDraftInput>(window, "LeadInMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            box.Text = "invalid";

            UiTestActions.Click(window, "AssociateTimingPostProcessorButton");

            Assert.Empty(requests);
            Assert.True(window.ViewModel.HasError);
            box.Text = "120";
            UiTestActions.Click(window, "AssociateTimingPostProcessorButton");
            var request = Assert.Single(requests);
            Assert.Equal(custom.Id, Assert.Single(request.Ids));
            Assert.Equal(120, request.Options.LeadInMilliseconds);
            model.IsBusy = true;
            Assert.False(UiTestActions.Find<Button>(window, "AssociateTimingPostProcessorButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(window, "UnlinkTimingPostProcessorButton").IsEffectivelyEnabled);
            model.IsBusy = false;
            Assert.True(UiTestActions.Find<CheckBox>(window, "TimingPostProcessorKeyframeSnapCheck").IsEffectivelyEnabled);
            Assert.True(model.KeyframeSnapEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", false, 980, 720)]
    [InlineData("en-US", false, 860, 580)]
    [InlineData("zh-CN", true, 980, 720)]
    [InlineData("zh-CN", true, 860, 580)]
    public void TimingPageRendersMixedTextAndExposesEveryFieldAtNormalAndMinimumWindowSize(string language,
        bool dark, int width, int height)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new SettingsWindow(new() { Language = language })
        {
            Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TIMING_POST_PROCESSOR);
            window.ViewModel.TimingPostProcessor.UpdateStyles([new(Guid.NewGuid(), "Default", new()), new(Guid.NewGuid(), "对白 中文 ABC 123", new())]);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsPage.TIMING_POST_PROCESSOR, UiTestActions.Find<ListBox>(window, "Navigation").SelectedValue);
            var content = UiTestActions.Find<Grid>(window, "TimingPostProcessorContentGrid");
            var stylesCard = UiTestActions.Find<Border>(window, "TimingPostProcessorStylesCard");
            var list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
            Assert.Equal(content.Bounds.Height, stylesCard.Bounds.Height, 1);
            var cardGrid = Assert.IsType<Grid>(stylesCard.Child);
            var header = Assert.Single(cardGrid.Children.OfType<TextBlock>());
            var footer = Assert.Single(cardGrid.Children, child => Grid.GetRow(child) == 2);
            var availableListHeight = stylesCard.Bounds.Height - stylesCard.Padding.Top - stylesCard.Padding.Bottom
                - stylesCard.BorderThickness.Top - stylesCard.BorderThickness.Bottom - header.Bounds.Height
                - footer.Bounds.Height - cardGrid.RowSpacing * 2;
            Assert.Equal(availableListHeight, list.Bounds.Height, 1);
            Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>(), item => Assert.InRange(item.Bounds.Height, 24, 30));
            var selectAll = UiTestActions.Find<Button>(window, "SelectAllTimingStylesButton");
            var selectNone = UiTestActions.Find<Button>(window, "SelectNoneTimingStylesButton");
            Assert.Equal(selectAll.Bounds.Width, selectAll.Bounds.Height, 1);
            Assert.Equal(selectNone.Bounds.Width, selectNone.Bounds.Height, 1);
            UiTestActions.Click(window, "SelectAllTimingStylesButton");
            Assert.All(window.ViewModel.TimingPostProcessor.Styles, style => Assert.True(style.IsSelected));
            UiTestActions.Click(window, "SelectNoneTimingStylesButton");
            Assert.All(window.ViewModel.TimingPostProcessor.Styles, style => Assert.False(style.IsSelected));
            foreach (var name in new[]
                     {
                         "LeadInMillisecondsInput", "LeadOutMillisecondsInput", "MaximumGapMillisecondsInput",
                         "MaximumOverlapMillisecondsInput", "StartBeforeMillisecondsInput", "StartAfterMillisecondsInput",
                         "EndBeforeMillisecondsInput", "EndAfterMillisecondsInput"
                     })
            {
                var input = UiTestActions.Find<NumericDraftInput>(window, name);
                Assert.True(input.IsEffectivelyVisible);
                Assert.True(input.Bounds.Width >= 60);
                Assert.False(string.IsNullOrWhiteSpace(input.RawText));
            }

            var apply = UiTestActions.Find<Button>(window, "AssociateTimingPostProcessorButton");
            apply.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Assert.True(Path.IsPathFullyQualified(directory));
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"timing-post-processor-{language}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static WorkbenchSession CreateSession(Window owner, DesktopApplicationContext context,
        ProjectEditor editor, Func<string, int, MediaTime, CancellationToken, Task<VideoTimingIndex>>? probe = null)
    {
        return new(new WindowWorkbenchDialogService(owner), update => new VideoPreviewController(
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(5)));
                }, (_, _) => new(_ => new PreviewTestSource(7, 0, 1000, 2000, 3000, 4000)),
                () => new UiPreviewConverter(), Dispatch, update),
            Dispatch, editor, applicationContext: context, videoTimingProbe: probe);
    }

    private static CheckBox FindTimingStyleCheckbox(SettingsWindow window, Guid styleId)
    {
        var list = UiTestActions.Find<ListBox>(window, "TimingPostProcessorStylesList");
        var checkbox = list.GetVisualDescendants().OfType<CheckBox>().Single(control =>
            control.DataContext is TimingStyleChoice choice && choice.Id == styleId);
        checkbox.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return checkbox;
    }

    private static ProjectDocument CreateDocument()
    {
        var line = new SubtitleLine { Start = new(2), End = new(3), StyleName = "Default", Text = "对白 中文 ABC 123" };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }

    private static async Task Dispatch(Action action, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, token);
    }
}
