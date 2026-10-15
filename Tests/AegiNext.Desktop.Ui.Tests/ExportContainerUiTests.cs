using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Settings.Export;
using AegiNext.Export;
using AegiNext.Export.Presets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证压制与预设编辑器的格式选择、草稿保护及共享属性行布局。</summary>
public sealed class ExportContainerUiTests
{
    /// <summary>编码器决定格式选项，并保留当前兼容的 MP4 或回退到 MKV。</summary>
    [AvaloniaTheory]
    [InlineData(VideoCodec.Auto, VideoContainerFormat.MP4, false, true)]
    [InlineData(VideoCodec.H264, VideoContainerFormat.MP4, false, true)]
    [InlineData(VideoCodec.Hevc, VideoContainerFormat.MP4, false, true)]
    [InlineData(VideoCodec.Vp8, VideoContainerFormat.MKV, true, false)]
    [InlineData(VideoCodec.Vp9, VideoContainerFormat.MP4, true, true)]
    [InlineData(VideoCodec.Av1, VideoContainerFormat.MP4, true, true)]
    public async Task CodecSelectionRefreshesContainerChoices(VideoCodec codec, VideoContainerFormat expected,
        bool webm, bool mp4)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var model = context.Session.ViewModel.Export;

        view.FindControl<ComboBox>("CodecCombo")!.SelectedItem = Assert.Single(model.Codecs, value => value.Codec == codec);
        context.Flush();

        var selector = view.FindControl<ComboBox>("ContainerCombo")!;
        var formats = selector.Items.Cast<VideoContainerChoice>().Select(value => value.Container).ToArray();
        Assert.Contains(VideoContainerFormat.MKV, formats);
        Assert.Equal(mp4, formats.Contains(VideoContainerFormat.MP4));
        Assert.Equal(webm, formats.Contains(VideoContainerFormat.WEBM));
        Assert.DoesNotContain(VideoContainerFormat.AUTO, formats);
        Assert.Equal(expected, Assert.IsType<VideoContainerChoice>(selector.SelectedItem).Container);
        Assert.Equal(expected, model.SelectedContainer);
        Assert.Equal(expected, model.CaptureSettings(true).Container);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>真实下拉选择 MKV 后切换编码器保留格式，不兼容 WebM 则回退到 MP4。</summary>
    [AvaloniaFact]
    public async Task PointerSelectedFormatSurvivesCompatibleCodecsAndFallsBackWhenIncompatible()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var model = context.Session.ViewModel.Export;
        var selector = view.FindControl<ComboBox>("ContainerCombo")!;
        SelectContainer(context.Owner, selector, VideoContainerFormat.MKV);

        foreach (var codec in new[] { VideoCodec.Vp8, VideoCodec.Vp9, VideoCodec.Av1, VideoCodec.Hevc })
        {
            view.FindControl<ComboBox>("CodecCombo")!.SelectedItem = Assert.Single(model.Codecs, value => value.Codec == codec);
            context.Flush();
            Assert.Equal(VideoContainerFormat.MKV, Assert.IsType<VideoContainerChoice>(selector.SelectedItem).Container);
            Assert.Equal(VideoContainerFormat.MKV, model.SelectedContainer);
        }
        model.SelectedCodec = VideoCodec.Vp9;
        context.Flush();
        SelectContainer(context.Owner, selector, VideoContainerFormat.WEBM);
        model.SelectedCodec = VideoCodec.H264;
        context.Flush();

        Assert.Equal(VideoContainerFormat.MP4, model.SelectedContainer);
        Assert.Equal(VideoContainerFormat.MP4, Assert.IsType<VideoContainerChoice>(selector.SelectedItem).Container);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>选择 WebM 会将 AAC 转为 Opus，并在当前界面显示转换提示。</summary>
    [AvaloniaFact]
    public async Task WebMSelectionUpdatesAudioControlAndDisplaysConversionHint()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        var model = context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var audio = view.FindControl<ComboBox>("AudioModeCombo")!;
        audio.SelectedIndex = (int)AudioExportMode.Aac;
        context.Flush();
        var hint = view.FindControl<TextBlock>("AudioCompatibilityHint")!;
        Assert.False(hint.IsEffectivelyVisible);

        SelectContainer(context.Owner, view.FindControl<ComboBox>("ContainerCombo")!, VideoContainerFormat.WEBM);

        Assert.Equal((int)AudioExportMode.Opus, audio.SelectedIndex);
        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Equal(Localization.Get("Workbench.WebMAudioConvertedHint"), hint.Text);
        SelectContainer(context.Owner, view.FindControl<ComboBox>("ContainerCombo")!, VideoContainerFormat.MKV);
        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.False(hint.IsEffectivelyVisible);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>面板和独立预设编辑器刷新语言时保留格式身份及所有无效草稿。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LanguageRefreshPreservesContainerIdentityAndUnfinishedDrafts(bool presetEditor)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var settings = new ExportSettingsViewModel();
        settings.UpdatePresets([new(Guid.NewGuid(), "Stable", VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with
        {
            Container = VideoContainerFormat.WEBM,
            AudioMode = AudioExportMode.Opus
        })]);
        VideoExportSettingsViewModel model = presetEditor ? settings : context.Session.ViewModel.Export;
        model.SelectedCodec = VideoCodec.Vp9;
        model.AudioMode = (int)AudioExportMode.Opus;
        model.SelectedContainer = VideoContainerFormat.WEBM;
        using var panel = presetEditor ? null : new ExportPanelView(context.Session.ViewModel.Export, context.Session);
        Control view = presetEditor ? new ExportSettingsView { DataContext = settings } : panel!;
        var host = new Window { Width = presetEditor ? 820 : 420, Height = 1000, Content = view };
        try
        {
            host.Show();
            Flush(host);
            var selector = view.FindControl<ComboBox>("ContainerCombo")!;
            var selected = selector.SelectedItem;
            view.FindControl<NumericDraftInput>("QualityInput")!.RawText = "7e-";
            view.FindControl<NumericDraftInput>("VideoBitrateInput")!.RawText = "unfinished video";
            view.FindControl<NumericDraftInput>("AudioBitrateInput")!.RawText = "unfinished audio";

            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(language);
                if (presetEditor)
                {
                    settings.RefreshLanguage();
                }
                Flush(host);
                Assert.Same(selected, selector.SelectedItem);
                Assert.Equal(VideoContainerFormat.WEBM, model.SelectedContainer);
                Assert.Equal("7e-", model.QualityText);
                Assert.Equal("unfinished video", model.VideoBitrateText);
                Assert.Equal("unfinished audio", model.AudioBitrateText);
                Assert.Equal("7e-", view.FindControl<NumericDraftInput>("QualityInput")!.RawText);
                Assert.Equal("unfinished video", view.FindControl<NumericDraftInput>("VideoBitrateInput")!.RawText);
                Assert.Equal("unfinished audio", view.FindControl<NumericDraftInput>("AudioBitrateInput")!.RawText);
                Assert.Equal(Localization.Get("Workbench.OutputFormat"), view.FindControl<PropertyRowControl>("ContainerRow")!.Header);
                Assert.Equal(Localization.Get("Workbench.WebMAudioHint"), view.FindControl<TextBlock>("AudioCompatibilityHint")!.Text);
            }
            Assert.False(context.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.DataContext = null;
        }
    }

    /// <summary>仅在预设页切换格式即可保存原预设，丢弃后恢复保存的格式。</summary>
    [AvaloniaFact]
    public async Task PresetEditorSavesAndDiscardsContainerOnlyEdits()
    {
        using var environment = new UiTestEnvironment();
        var original = new VideoExportPreset(Guid.NewGuid(), "Container only", new() { Container = VideoContainerFormat.MP4 });
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([original]);
        VideoExportPreset? committed = null;
        model.SaveDraftAsync = preset =>
        {
            committed = preset;
            model.UpdatePresets([preset], preset.Id);
            return Task.FromResult(true);
        };
        var view = new ExportSettingsView { DataContext = model };
        var host = new Window { Width = 860, Height = 1000, Content = view };
        try
        {
            host.Show();
            Flush(host);
            var selector = view.FindControl<ComboBox>("ContainerCombo")!;
            SelectContainer(host, selector, VideoContainerFormat.MKV);
            Assert.True(model.IsDirty);
            UiTestActions.Click(host, "SaveExportPresetButton");
            await model.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(committed);
            Assert.Equal(original with { Settings = original.Settings with { Container = VideoContainerFormat.MKV } }, committed);
            Assert.Equal(original.Id, model.SelectedPreset?.Id);
            Assert.False(model.IsDirty);
            SelectContainer(host, selector, VideoContainerFormat.MP4);
            Assert.True(model.IsDirty);
            model.DiscardDraft();
            Flush(host);
            Assert.False(model.IsDirty);
            Assert.Equal(VideoContainerFormat.MKV, Assert.IsType<VideoContainerChoice>(selector.SelectedItem).Container);
        }
        finally
        {
            host.Close();
            view.DataContext = null;
        }
    }

    /// <summary>两处格式行与相邻下拉左右对齐，深浅主题和宽窄窗口均能完整显示格式选项。</summary>
    [AvaloniaTheory]
    [InlineData(280, false, false)]
    [InlineData(280, true, false)]
    [InlineData(640, false, false)]
    [InlineData(640, true, false)]
    [InlineData(280, false, true)]
    [InlineData(280, true, true)]
    [InlineData(640, false, true)]
    [InlineData(640, true, true)]
    public async Task ContainerRowsKeepAlignedUnclippedChoicesAcrossThemesAndWidths(int editorWidth, bool dark, bool presetEditor)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        Localization.SetLanguage(dark ? "zh-CN" : "en-US");
        var settings = new ExportSettingsViewModel();
        settings.UpdatePresets([new(Guid.NewGuid(), "Geometry", VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9))]);
        context.Session.ViewModel.Export.SelectedCodec = VideoCodec.Vp9;
        using var panel = presetEditor ? null : new ExportPanelView(context.Session.ViewModel.Export, context.Session);
        Control view = presetEditor ? new ExportSettingsView { DataContext = settings } : panel!;
        var host = new Window
        {
            Width = editorWidth + (presetEditor ? 196 : 0),
            Height = 1000,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            host.Show();
            Flush(host);
            var selector = view.FindControl<ComboBox>("ContainerCombo")!;
            selector.BringIntoView();
            Flush(host);
            var row = Assert.Single(selector.GetVisualAncestors().OfType<PropertyRowControl>());
            var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == Localization.Get("Workbench.OutputFormat"));
            var codec = view.FindControl<ComboBox>("CodecCombo")!;
            var selectorPoint = selector.TranslatePoint(default, view)!.Value;
            var codecPoint = codec.TranslatePoint(default, view)!.Value;
            var titlePoint = title.TranslatePoint(default, view)!.Value;
            Assert.InRange(Math.Abs(selectorPoint.X - codecPoint.X), 0, 0.5);
            Assert.InRange(Math.Abs(selector.Bounds.Width - codec.Bounds.Width), 0, 0.5);
            Assert.True(selectorPoint.Y >= titlePoint.Y + title.Bounds.Height);
            Assert.InRange(selector.Bounds.Width, 100, row.Bounds.Width);
            Assert.Equal(11, title.FontSize);
            Assert.Equal(host.ActualThemeVariant, selector.ActualThemeVariant);

            OpenDropdown(host, selector);
            foreach (var index in Enumerable.Range(0, selector.ItemCount))
            {
                var item = Assert.IsType<ComboBoxItem>(selector.ContainerFromIndex(index));
                var popupRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
                var itemPoint = item.TranslatePoint(default, popupRoot)!.Value;
                var label = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>());
                var labelPoint = label.TranslatePoint(default, item)!.Value;
                Assert.InRange(itemPoint.X, -0.5, popupRoot.Bounds.Width);
                Assert.InRange(itemPoint.Y, -0.5, popupRoot.Bounds.Height);
                Assert.True(itemPoint.X + item.Bounds.Width <= popupRoot.Bounds.Width + 0.5);
                Assert.True(itemPoint.Y + item.Bounds.Height <= popupRoot.Bounds.Height + 0.5);
                Assert.True(label.Bounds.Width > 0);
                Assert.InRange(labelPoint.X, 0, item.Bounds.Width);
                Assert.True(labelPoint.X + label.DesiredSize.Width <= item.Bounds.Width + 0.5);
                Assert.Equal(host.ActualThemeVariant, item.ActualThemeVariant);
            }
        }
        finally
        {
            view.FindControl<ComboBox>("ContainerCombo")!.IsDropDownOpen = false;
            host.Close();
            view.DataContext = null;
        }
    }

    private static void SelectContainer(Window host, ComboBox selector, VideoContainerFormat container)
    {
        OpenDropdown(host, selector);
        var choice = selector.Items.Cast<VideoContainerChoice>().Single(value => value.Container == container);
        var item = Assert.IsType<ComboBoxItem>(selector.ContainerFromIndex(selector.Items.IndexOf(choice)));
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Flush(host);
        Assert.Equal(container, Assert.IsType<VideoContainerChoice>(selector.SelectedItem).Container);
    }

    private static void OpenDropdown(Window host, ComboBox selector)
    {
        selector.BringIntoView();
        Flush(host);
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(selector));
        var point = selector.TranslatePoint(new Point(selector.Bounds.Width / 2, selector.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Flush(host);
        Assert.True(selector.IsDropDownOpen);
    }

    private static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
