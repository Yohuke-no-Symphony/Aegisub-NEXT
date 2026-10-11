using AegiNext.Application;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeStylePresetUiTests
{
    [AvaloniaFact]
    public async Task SharedPresetSelectorAppliesBodyStyleExplicitlyAndHighlightStyleImmediately()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "共用样式", new() { FontSize = 96, Fill = new(2, 0.25, 0.5) });
        await context.Session.Styles.UpsertAsync(preset);
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        var input = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        input.SetSelection(0, 1);
        var target = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        Assert.Equal(0, target.SelectedIndex);
        var selector = UiTestActions.Find<ComboBox>(host, "SelectionStylePresetCombo");
        selector.SelectedItem = selector.Items.OfType<StylePresetListItem>().Single(value => value.Id == preset.Id);
        Assert.Same(original, context.Session.DocumentSnapshot);
        UiTestActions.Click(host, "ApplyToSelectionButton");
        var bodySnapshot = context.Session.DocumentSnapshot;
        var body = Assert.Single(bodySnapshot.Subtitles);
        Assert.Equal(original.Subtitles[0].Style, body.Style);
        var selectedStyle = Assert.Single(body.InlineSpans);
        Assert.Equal(0, selectedStyle.Utf16Start);
        Assert.Equal(1, selectedStyle.Utf16Length);
        Assert.Equal(96d, selectedStyle.Style.FontSize);
        input.SetSelection(0, 0);
        target.SelectedIndex = 2;
        var previousHighlightPresetId = Assert.IsType<StylePresetListItem>(selector.SelectedItem).Id;
        context.Session.Details.EditDuration("invalid");
        selector.SelectedItem = selector.Items.OfType<StylePresetListItem>().Single(value => value.Id == preset.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(bodySnapshot, context.Session.DocumentSnapshot);
        Assert.Equal("invalid", context.Session.Details.DurationText);
        Assert.Equal(previousHighlightPresetId, Assert.IsType<StylePresetListItem>(selector.SelectedItem).Id);
        context.Session.Details.Restore("Duration");
        selector.SelectedItem = selector.Items.OfType<StylePresetListItem>().Single(value => value.Id == preset.Id);
        var highlight = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, preset.Style), highlight.KaraokeStyle);
        Assert.Equal(body.Style, highlight.Style);
        Assert.Equal(body.InlineSpans, highlight.InlineSpans);
        Assert.Equal(body.Karaoke, highlight.Karaoke);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(bodySnapshot, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task PresetSelectionLanguageRefreshAndApplicationPreserveTypographyAndExistingTiming()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "高亮 HDR", new()
        {
            FontFamily = "Different font", FontSize = 200, Fill = new(4, 0.2, 2, 0.6),
            Stroke = new(0, 1, 0), StrokeWidth = 5, ShadowColor = new(0, 0, 1, 0.5), ShadowOffset = new(-4, 7)
        });
        await context.Session.Styles.UpsertAsync(preset);
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab😀");
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { FontFamily = "Noto Sans CJK SC", FontSize = 42 },
            Karaoke = [new(0, 2, new(1, 5), new(7, 10), new(1, 0.6, 0))]
        });
        context.Session.SelectCue(id);
        var original = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        var selector = UiTestActions.Find<ComboBox>(host, "SelectionStylePresetCombo");
        selector.SelectedItem = selector.Items.OfType<StylePresetListItem>().Single(value => value.Id == preset.Id);
        var appliedSnapshot = context.Session.DocumentSnapshot;
        var applied = Assert.Single(appliedSnapshot.Subtitles);
        Assert.Equal(KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, preset.Style), applied.KaraokeStyle);
        Assert.Equal(original.Subtitles[0].Karaoke, applied.Karaoke);
        Assert.Equal(original.Subtitles[0].Style, applied.Style);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "en-US" });
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        Assert.Equal(preset.Id, Assert.IsType<StylePresetListItem>(selector.SelectedItem).Id);
        Assert.Same(appliedSnapshot, context.Session.DocumentSnapshot);
        Assert.Empty(context.Session.DocumentSnapshot.Assets);
        Assert.Equal(applied.KaraokeStyle, Assert.Single(ProjectStore.Deserialize(ProjectStore.Serialize(context.Session.DocumentSnapshot)).Subtitles).KaraokeStyle);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());

        var frozen = context.Session.DocumentSnapshot;
        await context.Session.Styles.DeleteAsync(preset.Id);
        Assert.Same(frozen, context.Session.DocumentSnapshot);
        Assert.Equal(preset.Id, Assert.IsType<StylePresetListItem>(selector.SelectedItem).Id);
        Assert.Contains(selector.Items.OfType<StylePresetListItem>(), value => value.Id == preset.Id);
        UiTestActions.Find<ToggleButton>(host, "EnableKaraokeToggle").IsChecked = false;
        Assert.Empty(Assert.Single(context.Session.DocumentSnapshot.Subtitles).Karaoke);
        Assert.Equal(applied.KaraokeStyle, Assert.Single(context.Session.DocumentSnapshot.Subtitles).KaraokeStyle);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(frozen, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task DefaultChoiceRetainsLegacyHighlightAndNewGenerationRespectsGraphemeBoundaries()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "😀e\u0301");
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        UiTestActions.Click(host, "EnableKaraokeToggle");
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        var selector = UiTestActions.Find<ComboBox>(host, "SelectionStylePresetCombo");
        Assert.Equal(Guid.Empty, Assert.IsType<StylePresetListItem>(selector.SelectedItem).Id);

        var line = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Null(line.KaraokeStyle);
        int[] starts = [0, 2];
        int[] lengths = [2, 2];
        Assert.Equal(starts, line.Karaoke.Select(value => value.Utf16Start));
        Assert.Equal(lengths, line.Karaoke.Select(value => value.Utf16Length));
        Assert.All(line.Karaoke, value => Assert.Equal(new SceneColor(1, 0.6, 0), value.HighlightColor));
        ProjectValidator.Validate(context.Session.DocumentSnapshot);
    }
}
