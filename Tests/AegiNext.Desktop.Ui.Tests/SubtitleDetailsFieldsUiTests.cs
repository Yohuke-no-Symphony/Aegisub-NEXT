using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;
using Material.Icons;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsFieldsUiTests
{
    [AvaloniaFact]
    public async Task BodyAndHighlightShadowAxesShareOneRowAndRetainExactDrafts()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { ShadowOffset = new(-1.1234567891234567, 7.1234567891234567) }
        });
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.Editor.Snapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        Flush(host);
        foreach (var highlight in new[] { false, true })
        {
            UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = highlight ? 2 : 0;
            var grid = UiTestActions.Find<Grid>(host, "SelectionShadowOffsetInput");
            foreach (var expander in grid.GetLogicalAncestors().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }
            Flush(host);
            Assert.Equal(4, grid.ColumnDefinitions.Count);
            var x = UiTestActions.Find<NumericDraftInput>(host, "SelectionShadowXInput");
            var y = UiTestActions.Find<NumericDraftInput>(host, "SelectionShadowYInput");
            Assert.Same(grid, x.Parent);
            Assert.Same(grid, y.Parent);
            Assert.Equal(1, Grid.GetColumn(x));
            Assert.Equal(3, Grid.GetColumn(y));
            var xBounds = BoundsIn(x, grid);
            var yBounds = BoundsIn(y, grid);
            Assert.True(xBounds.Width > 0 && yBounds.Width > 0);
            Assert.InRange(Math.Abs(xBounds.Center.Y - yBounds.Center.Y), 0, 1);
            Assert.True(xBounds.Right <= yBounds.Left);
            Assert.False(x.ShowButtonSpinner);
            Assert.False(y.ShowButtonSpinner);
            Assert.Equal(original.Subtitles[0].Style.ShadowOffset.X, double.Parse(x.RawText, CultureInfo.InvariantCulture));
            Assert.Equal(original.Subtitles[0].Style.ShadowOffset.Y, double.Parse(y.RawText, CultureInfo.InvariantCulture));
        }
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.False(context.Session.Details.HighlightDraft.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData(300, "en-US")]
    [InlineData(300, "zh-CN")]
    [InlineData(950, "en-US")]
    [InlineData(950, "zh-CN")]
    public async Task SelectionActionsAreSquareIconsBesideEmphasisWithLocalizedDescriptions(double width, string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = width;
        host.Height = 900;
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        Flush(host);
        var toolbar = UiTestActions.Find<Panel>(host, "SelectionStyleToolbar");
        var separator = UiTestActions.Find<Separator>(host, "SelectionActionSeparator");
        Assert.InRange(Math.Abs(separator.Bounds.Width - 1), 0, 0.5);
        Assert.InRange(Math.Abs(separator.Bounds.Height - 24), 0, 1);
        Assert.True(separator.Bounds.Height > separator.Bounds.Width);
        var names = new[] { "BoldSelectionButton", "ItalicSelectionButton", "UnderlineSelectionButton", "StrikethroughSelectionButton",
            "ApplyToSelectionButton", "ClearSelectionStyleButton" };
        var buttons = names.Select(name => UiTestActions.Find<Button>(host, name)).ToArray();
        var centerY = BoundsIn(buttons[0], toolbar).Center.Y;
        var previousRight = double.NegativeInfinity;
        foreach (var button in buttons)
        {
            var bounds = BoundsIn(button, toolbar);
            Assert.InRange(Math.Abs(bounds.Width - bounds.Height), 0, 1);
            Assert.True(bounds.Width >= 24);
            Assert.InRange(Math.Abs(bounds.Center.Y - centerY), 0, 1);
            Assert.True(previousRight <= bounds.Left);
            previousRight = bounds.Right;
        }
        var separatorBounds = BoundsIn(separator, toolbar);
        Assert.True(BoundsIn(buttons[3], toolbar).Right <= separatorBounds.Left);
        Assert.True(separatorBounds.Right <= BoundsIn(buttons[4], toolbar).Left);
        foreach (var key in new[] { "ApplyToSelection", "ClearSelectionStyle" })
        {
            var button = UiTestActions.Find<Button>(host, key + "Button");
            Assert.IsType<MaterialIcon>(button.Content);
            Assert.Equal(Localization.Get("Workbench." + key + "Hint"), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("Workbench." + key), AutomationProperties.GetName(button));
        }
        var original = context.Session.Editor.Snapshot;
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language == "zh-CN" ? "en-US" : "zh-CN" });
        Flush(host);
        foreach (var key in new[] { "ApplyToSelection", "ClearSelectionStyle" })
        {
            var button = UiTestActions.Find<Button>(host, key + "Button");
            Assert.Equal(Localization.Get("Workbench." + key + "Hint"), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("Workbench." + key), AutomationProperties.GetName(button));
        }
        Assert.Same(original, context.Session.Editor.Snapshot);
    }

    [AvaloniaTheory]
    [InlineData(950, false)]
    [InlineData(950, true)]
    [InlineData(460, false)]
    [InlineData(460, true)]
    public async Task FieldGroupsAndHighlightControlsRemainReadableInLightAndDarkWindows(double width, bool dark)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT, Language = "zh-CN" });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "你好 Karaoke 👩‍💻");
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = width;
        host.Height = width >= 950 ? 1200 : 900;
        Flush(host);
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 2);
        rich.Focus();
        Flush(host);
        Assert.Null(rich.RenderDiagnostic);
        var groups = UiTestActions.Find<WrapPanel>(host, "SelectionStyleFields");
        Assert.True(groups.IsEffectivelyVisible);
        Assert.All(groups.Children, child => Assert.True(child.Bounds.Width >= 190));
        Assert.True(UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput").Bounds.Width >= 170);
        AssertUnifiedScroll(host, groups);
        AssertUnifiedScroll(host, rich);
        Capture(host, $"details-revision-{(dark ? "dark" : "light")}-{width}-fields.png");
        rich.SetSelection(0, 0);
        Assert.False(groups.IsEffectivelyEnabled);
        Assert.False(UiTestActions.Find<ColorDraftInput>(host, "SelectionFillInput").IsEffectivelyEnabled);
        Capture(host, $"details-revision-{(dark ? "dark" : "light")}-{width}-disabled.png");
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        Flush(host);
        Assert.True(groups.IsEffectivelyVisible);
        Assert.Same(context.Session.Details.HighlightDraft.Fill, UiTestActions.Find<ColorDraftInput>(host, "SelectionFillInput").Draft);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        Assert.True(axis.IsEffectivelyVisible);
        AssertUnifiedScroll(host, rich);
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        var snap = UiTestActions.Find<ToolbarToggleButton>(host, "KaraokeSnapToggle");
        Assert.Equal(MaterialIconKind.Magnet, Assert.IsType<MaterialIcon>(snap.Content).Kind);
        Assert.DoesNotContain(Assert.IsAssignableFrom<Control>(popup.Child).GetLogicalDescendants().OfType<Control>(),
            control => control.Name == "KaraokeSnapToggle");
        var snapshot = context.Session.Editor.Snapshot;
        Assert.True(snap.IsChecked);
        Assert.True(axis.IsSnapEnabled);
        snap.IsChecked = false;
        Assert.False(axis.IsSnapEnabled);
        snap.IsChecked = true;
        Assert.True(axis.IsSnapEnabled);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Capture(host, $"details-revision-{(dark ? "dark" : "light")}-{width}-highlight.png");
    }

    [AvaloniaFact]
    public async Task OneTransportButtonPlaysPausesAndLoopToggleDoesNotAutoplay()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 1, new(1), new(2), new(1, 0.6, 0)), new(1, 1, new(2), new(3), new(1, 0.6, 0))]
        });
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Flush(host);
        var loop = UiTestActions.Find<ToggleButton>(host, "SubtitleLoopToggle");
        var play = UiTestActions.Find<Button>(host, "SubtitlePlayPauseButton");
        var initialPlayKind = Assert.IsType<MaterialIcon>(play.Content).Kind;
        AssertPlayDescription(play, false);
        Assert.InRange(Math.Abs(play.Bounds.Width - play.Bounds.Height), 0, 1);
        Assert.False(loop.IsChecked);
        loop.IsChecked = true;
        Assert.False(context.Controller.IsRangePlaybackActive);
        loop.IsChecked = false;
        Assert.False(context.Controller.IsRangePlaybackActive);
        UiTestActions.Click(host, "SubtitlePlayPauseButton");
        await DrainAsync(() => context.Session.Details.IsPlaying);
        Assert.NotEqual(initialPlayKind, Assert.IsType<MaterialIcon>(play.Content).Kind);
        AssertPlayDescription(play, true);
        loop.IsChecked = true;
        Assert.True(context.Session.Details.IsPlaying);
        UiTestActions.Click(host, "SubtitlePlayPauseButton");
        await DrainAsync(() => !context.Session.Details.IsPlaying);
        Assert.Equal(initialPlayKind, Assert.IsType<MaterialIcon>(play.Content).Kind);
        AssertPlayDescription(play, false);
        var clip = context.Session.Editor.Snapshot.Subtitles[0].Karaoke[0];
        Assert.True(context.Session.Details.SelectClip(clip.Id));
        loop.IsChecked = false;
        UiTestActions.Click(host, "SubtitlePlayPauseButton");
        await DrainAsync(() => context.Session.Details.IsPlaying);
        Assert.Equal(new MediaTime(1), context.Controller.Snapshot.Position);
        await context.Clock.WaitForScheduledTimerAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        await DrainAsync(() => !context.Session.Details.IsPlaying);
        await DrainAsync(() => Equals(AutomationProperties.GetName(play), Localization.Get("Workbench.PlaySegment")));
        Assert.Equal(initialPlayKind, Assert.IsType<MaterialIcon>(play.Content).Kind);
        AssertPlayDescription(play, false);
        loop.IsChecked = false;
        Assert.False(context.Controller.IsRangePlaybackActive);
    }

    [AvaloniaFact]
    public async Task NarrowFloatingWindowWrapsCompleteFieldGroupsAndKeepsTextSpace()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "你好👩‍💻");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 300;
        host.Height = 400;
        Flush(host);
        var body = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        body.SetSelection(0, 2);
        var fields = UiTestActions.Find<WrapPanel>(host, "SelectionStyleFields");
        Assert.All(fields.Children, child => Assert.IsType<StackPanel>(child));
        AssertUnifiedScroll(host, fields);
        AssertUnifiedScroll(host, body);
        Assert.DoesNotContain(host.GetLogicalDescendants().OfType<Control>(), control => control.Name is "SelectionStyleExpander" or "SubtitleRichTextScroll");
        var scroll = UiTestActions.Find<ScrollViewer>(host, "SubtitleDetailsScroll");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        var toolbar = UiTestActions.Find<Grid>(host, "SubtitleDetailsToolbar");
        var toolbarScroll = UiTestActions.Find<ScrollViewer>(host, "SelectionToolbarScroll");
        var enable = UiTestActions.Find<ToolbarToggleButton>(host, "EnableKaraokeToggle");
        var timingActions = UiTestActions.Find<WrapPanel>(host, "KaraokeTimingActions");
        Assert.Same(enable, timingActions.Children[0]);
        Assert.Equal(ScrollBarVisibility.Disabled, toolbarScroll.VerticalScrollBarVisibility);
        Assert.True(toolbarScroll.Extent.Width > toolbarScroll.Viewport.Width);
        var toolbarBounds = BoundsIn(toolbar, host);
        Assert.InRange(Math.Abs(BoundsIn(toolbarScroll, host).Width - toolbarBounds.Width), 0, 1);
        Assert.Equal(0, enable.Bounds.Left);
        Assert.Equal(0, enable.Bounds.Top);
        Assert.True(body.IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis").IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").IsEffectivelyEnabled);
        Assert.False(UiTestActions.Find<ToggleButton>(host, "SubtitleLoopToggle").IsChecked);
        Assert.Single(host.GetVisualDescendants().OfType<Button>(), button => button.Name == "SubtitlePlayPauseButton");
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Capture(host, "details-toolbar-narrow-300.png");
    }

    [AvaloniaTheory]
    [InlineData(300, "en-US")]
    [InlineData(300, "zh-CN")]
    [InlineData(460, "en-US")]
    [InlineData(460, "zh-CN")]
    [InlineData(950, "en-US")]
    [InlineData(950, "zh-CN")]
    public async Task TimingToolbarStartsWithLabeledHighlightAndLocalizedMagnet(double width, string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Subtitle 中文 123");
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = width;
        host.Height = 900;
        Flush(host);
        var timingActions = UiTestActions.Find<WrapPanel>(host, "KaraokeTimingActions");
        var snap = UiTestActions.Find<ToolbarToggleButton>(host, "KaraokeSnapToggle");
        var enable = UiTestActions.Find<ToolbarToggleButton>(host, "EnableKaraokeToggle");
        Assert.Same(timingActions, snap.Parent);
        Assert.Equal(MaterialIconKind.Magnet, Assert.IsType<MaterialIcon>(snap.Content).Kind);
        Assert.Equal(32, snap.Bounds.Width);
        Assert.Equal(32, snap.Bounds.Height);
        Assert.Same(enable, timingActions.Children[0]);
        Assert.True(enable.Bounds.Width > enable.Bounds.Height);
        var enableContent = Assert.IsType<IconText>(enable.Content);
        Assert.Equal("EnableHighlight", enableContent.IconKey);
        foreach (var selectedLanguage in new[] { language, language == "zh-CN" ? "en-US" : "zh-CN" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = selectedLanguage });
            Flush(host);
            Assert.Equal(Localization.Get("Workbench.EnableKaraoke"), enableContent.Text);
            Assert.Equal(Localization.Get("Workbench.KaraokeSnapHint"), ToolTip.GetTip(snap));
            Assert.Equal(Localization.Get("Workbench.KaraokeSnap"), AutomationProperties.GetName(snap));
            Assert.Equal(Localization.Get("Workbench.EnableKaraokeHint"), ToolTip.GetTip(enable));
            Assert.Equal(enableContent.Text, AutomationProperties.GetName(enable));
            Assert.Equal(0, enable.Bounds.Left);
            Assert.Equal(0, enable.Bounds.Top);
            Assert.True(enable.Bounds.Right <= timingActions.Bounds.Width);
        }
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        UiTestActions.Click(host, "KaraokeSnapToggle");
        Assert.False(snap.IsChecked);
        Assert.False(axis.IsSnapEnabled);
        UiTestActions.Click(host, "KaraokeSnapToggle");
        Assert.True(snap.IsChecked);
        Assert.True(axis.IsSnapEnabled);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task NarrowHighlightWindowKeepsBothTheRichTextViewportAndClipAxisVisible()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "你好👩‍💻");
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 300;
        host.Height = 600;
        Flush(host);
        var target = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        Assert.IsNotType<CheckBox>(target);
        Assert.True(target.IsEffectivelyEnabled);
        target.SelectedIndex = 2;
        Flush(host);
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        AssertUnifiedScroll(host, rich);
        AssertUnifiedScroll(host, axis);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.True(axis.Bounds.Height >= 88);
        axis.BringIntoView();
        Flush(host);
        var axisBounds = BoundsIn(axis, host);
        Assert.True(axisBounds.Top >= 0 && axisBounds.Bottom <= host.ClientSize.Height);
        Assert.DoesNotContain(host.GetLogicalDescendants().OfType<Control>(), control => control.Name is "SubtitleDetailsTabs" or "SubtitleCodeInput");
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Capture(host, "details-toolbar-narrow-highlight-300-600.png");
    }

    [AvaloniaFact]
    public async Task ActualWheelScrollsTheEntirePanelAndResizeCreatesNoTransaction()
    {
        await using var context = new MainWindowTestContext();
        var text = string.Join('\n', Enumerable.Range(0, 70).Select(index => "第 " + index.ToString(CultureInfo.InvariantCulture) + " 行：你好 👩‍💻"));
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), text);
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 460;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.Focus();
        rich.SetSelection(0, 1);
        Flush(host);
        var toolbar = UiTestActions.Find<Grid>(host, "SubtitleDetailsToolbar");
        var scroll = UiTestActions.Find<ScrollViewer>(host, "SubtitleDetailsScroll");
        AssertUnifiedScroll(host, rich);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var toolbarOrigin = toolbar.TranslatePoint(default, host)!.Value;
        var wheelPoint = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), host)!.Value;
        host.MouseWheel(wheelPoint, new(0, -3));
        Flush(host);
        Assert.True(scroll.Offset.Y > 0);
        Assert.True(toolbar.TranslatePoint(default, host)!.Value.Y < toolbarOrigin.Y);
        host.Width = 300;
        host.Height = 400;
        Flush(host);
        AssertUnifiedScroll(host, rich);
        Assert.Equal(0, rich.SelectionStart);
        Assert.Equal(1, rich.SelectionEnd);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.False(context.Session.Details.HighlightDraft.IsDirty);
    }

    [AvaloniaFact]
    public async Task ResizingKeepsEveryStyleFieldAvailableWithoutFolding()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "你好👩‍💻");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 2);
        rich.Focus();
        foreach (var size in new[] { new Size(300, 400), new Size(950, 1200), new Size(300, 400) })
        {
            host.Width = size.Width;
            host.Height = size.Height;
            Flush(host);
            var fields = UiTestActions.Find<WrapPanel>(host, "SelectionStyleFields");
            Assert.True(fields.IsEffectivelyVisible);
            AssertUnifiedScroll(host, fields);
            Assert.DoesNotContain(fields.GetVisualAncestors(), ancestor => ancestor is Expander);
        }
        Assert.Equal(0, rich.SelectionStart);
        Assert.Equal(2, rich.SelectionEnd);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.False(context.Session.Details.HighlightDraft.IsDirty);
    }

    [AvaloniaFact]
    public async Task ExplicitGenerationAndVisualStateExposeIndependentHighlightFieldsAndRetainTypographyWhenApplying()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { FontSize = 42 }, InlineSpans = [new(0, 1, new() { Bold = true })]
        });
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot.Subtitles[0];
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        var enable = UiTestActions.Find<ToggleButton>(host, "EnableKaraokeToggle");
        enable.BringIntoView();
        Flush(host);
        Assert.True(enable.IsEffectivelyEnabled);
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        Assert.True(enable.IsChecked);
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        var highlight = UiTestActions.Find<WrapPanel>(host, "SelectionStyleFields");
        Assert.True(highlight.IsEffectivelyVisible);
        Assert.True(highlight.IsEffectivelyEnabled);
        var fill = UiTestActions.Find<ColorDraftInput>(host, "SelectionFillInput");
        Assert.Same(context.Session.Details.HighlightDraft.Fill, fill.Draft);
        Assert.NotSame(context.Session.Details.StyleDraft.Fill, fill.Draft);
        Assert.False(UiTestActions.Find<FontFamilyPicker>(host, "SelectionFontInput").IsEffectivelyEnabled);
        var before = context.Session.Editor.Snapshot.Subtitles[0];
        context.Session.Details.HighlightDraft.Fill.SetValue(new(4, 0.25, 2, 0.6));
        var strokeWidth = UiTestActions.Find<NumericDraftInput>(host, "SelectionStrokeWidthInput");
        strokeWidth.RawText = "5.125";
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        strokeWidth.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var after = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Null(after.KaraokeStyle);
        Assert.Equal(new SceneColor(4, 0.25, 2, 0.6), KaraokeVisualStyleResolver.RangeStyleAt(after, 0, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.Equal(5.125, KaraokeVisualStyleResolver.RangeStyleAt(after, 0, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        Assert.Equal(original.Style, after.Style);
        Assert.Equal(original.InlineSpans, after.InlineSpans);
        Assert.Equal(before.Karaoke, after.Karaoke);
        Assert.True(context.Session.Editor.Undo());
        Assert.Equal(before, context.Session.Editor.Snapshot.Subtitles[0]);
        enable.IsChecked = false;
        Assert.Empty(context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(before.Karaoke.ToArray(), context.Session.Editor.Snapshot.Subtitles[0].InactiveKaraoke.ToArray());
        Assert.True(UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis").IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").IsEffectivelyEnabled);
        Assert.DoesNotContain(host.GetLogicalDescendants().OfType<Control>(), control => control.Name is
            "KaraokePropertiesExpander" or "KaraokeHighlightExpander" or "KaraokeHighlightFields" or "ApplyHighlightStyleButton" or "ApplyHighlightPresetButton");
        Assert.DoesNotContain(context.Window.Panels[WorkbenchPanelIds.STYLES].GetLogicalDescendants().OfType<Control>(),
            control => control.Name is "KaraokeStylePresetCombo" or "KaraokeButton" or "ClearKaraokeButton");
    }

    [AvaloniaTheory]
    [InlineData(300, "en-US")]
    [InlineData(300, "zh-CN")]
    [InlineData(950, "en-US")]
    [InlineData(950, "zh-CN")]
    public async Task ActualHighlightToggleRestoresStoredClipsAndTracksUndoRedoInNarrowAndWideWindows(double width, string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Ae\u0301👩‍💻");
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            InlineSpans = [new(1, 2, new() { Bold = true, FontSize = 27 })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.Parse("01d9209a-5df5-42c7-81a0-7b2a772c9845"), "Saved HDR", new()
            {
                Fill = new(4.123456789012345, -0.1, 2.75, 0.7312345678901234),
                StrokeWidth = 3.123456789012345,
                ShadowOffset = new(-1.123456789012345, 2.123456789012345)
            }),
            KaraokeStyleSpans =
            [
                new(0, 1, new() { StrokeWidth = 0 }, new() { Fill = new(0.25, 0.5, 2.75) }),
                new(1, 2, new() { Stroke = new(0.25, 3.75, 0.5), ShadowBlur = 2.125 }, new() { ShadowOffset = new(-2.5, 1.25) }),
                new(3, 5, new() { ShadowOffset = new(-3.125, 5.75), ShadowBlur = 1.625 }, new() { StrokeWidth = 1.125 })
            ],
            Karaoke =
            [
                new(0, 1, new(1, 7), new(5, 6), new(3.123456789012345, 0.25, 0.5, 0.6))
                {
                    Id = Guid.Parse("325265e9-3874-4f14-bbd0-b2c86ff12f3d"),
                    HighlightKind = KaraokeHighlightKind.STEP
                },
                new(1, 2, new(11, 12), new(17, 9), new(0.5, 2.123456789012345, 0.25))
                {
                    Id = Guid.Parse("53bdad57-d978-4597-9677-3de68f8a4d23"),
                    HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
                },
                new(3, 5, new(19, 9), new(31, 11), new(0.25, 0.5, 4.123456789012345))
                {
                    Id = Guid.Parse("ee3d799f-fca2-40a3-a9e0-9b5397d7cb12"),
                    HighlightKind = KaraokeHighlightKind.SWEEP
                }
            ]
        });
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        var expected = original.Subtitles[0];
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = width;
        host.Height = 900;
        Flush(host);
        var enable = UiTestActions.Find<ToggleButton>(host, "EnableKaraokeToggle");
        var target = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        Assert.True(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.False(context.Session.Editor.CanUndo);
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        var disabled = context.Session.Editor.Snapshot;
        Assert.False(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.True(target.IsEffectivelyEnabled);
        Assert.Empty(disabled.Subtitles[0].Karaoke);
        Assert.Equal(expected.Karaoke.ToArray(), disabled.Subtitles[0].InactiveKaraoke.ToArray());
        Assert.Equal(expected.KaraokeStyle, disabled.Subtitles[0].KaraokeStyle);
        Assert.Equal(expected.KaraokeStyleSpans, disabled.Subtitles[0].KaraokeStyleSpans);
        Assert.Equal(expected.InlineSpans, disabled.Subtitles[0].InlineSpans);
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        var restored = context.Session.Editor.Snapshot;
        Assert.True(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.True(target.IsEffectivelyEnabled);
        Assert.Equal(expected.Karaoke.ToArray(), restored.Subtitles[0].Karaoke.ToArray());
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(expected.KaraokeStyle, restored.Subtitles[0].KaraokeStyle);
        Assert.Equal(expected.KaraokeStyleSpans, restored.Subtitles[0].KaraokeStyleSpans);
        Assert.Equal(expected.Style, restored.Subtitles[0].Style);
        Assert.Equal(expected.InlineSpans, restored.Subtitles[0].InlineSpans);
        Assert.Equal(expected.Text, restored.Subtitles[0].Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(disabled, context.Session.Editor.Snapshot);
        Assert.False(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.True(context.Session.Editor.Redo());
        Flush(host);
        Assert.Same(restored, context.Session.Editor.Snapshot);
        Assert.True(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.True(context.Session.Editor.Undo());
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.True(enable.IsChecked);
        Assert.True(axis.IsEffectivelyVisible);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Rect BoundsIn(Control control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo) ?? throw new InvalidOperationException("Control is not attached to its expected toolbar.");
        return new(origin, control.Bounds.Size);
    }

    private static void AssertUnifiedScroll(Window host, Control control)
    {
        Flush(host);
        var scroll = UiTestActions.Find<ScrollViewer>(host, "SubtitleDetailsScroll");
        Assert.Contains(scroll, control.GetVisualAncestors());
        Assert.True(scroll.Viewport.Height > 0);
        Assert.True(control.Bounds.Height > 0);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
    }

    private static void AssertPlayDescription(Button play, bool playing)
    {
        var label = Localization.Get(playing ? "Workbench.Pause" : "Workbench.PlaySegment");
        Assert.Equal(label, ToolTip.GetTip(play));
        Assert.Equal(label, AutomationProperties.GetName(play));
    }

    private static async Task DrainAsync(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!complete())
        {
            Assert.True(DateTime.UtcNow < deadline, "Details playback did not reach its expected state.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        Flush(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
    }
}
