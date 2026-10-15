using AegiNext.Desktop.EffectLibrary;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectScriptPageLayoutUiTests
{
    private static readonly string[] toolbarButtons =
    [
        "AddEffectScriptButton", "DeleteEffectScriptButton", "ImportEffectScriptButton", "ExportEffectScriptButton",
        "ExportEffectScriptSourceButton", "ValidateEffectScriptButton", "SaveEffectScriptButton"
    ];

    [AvaloniaTheory]
    [InlineData(860, 580, "en-US", false)]
    [InlineData(860, 580, "en-US", true)]
    [InlineData(860, 580, "zh-CN", false)]
    [InlineData(860, 580, "zh-CN", true)]
    [InlineData(1200, 820, "en-US", false)]
    [InlineData(1200, 820, "en-US", true)]
    [InlineData(1200, 820, "zh-CN", false)]
    [InlineData(1200, 820, "zh-CN", true)]
    public void ScriptPageKeepsAllActionsInTheTopToolbarAndUsesItsRemainingHeight(double width, double height,
        string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new EffectLibraryWindow(new() { Language = language, Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT })
        {
            Width = width, Height = height
        };
        var saved = new List<EffectScriptPreset>();
        var deleted = new List<Guid>();
        var imports = 0;
        var exports = 0;
        window.UpsertEffectRequested += (_, args) =>
        {
            saved.Add(args.Preset);
            window.UpdateEffects(saved, args.Preset.Id);
        };
        window.DeleteEffectRequested += (_, args) =>
        {
            deleted.Add(args.Id);
            window.UpdateEffects(saved.Where(preset => preset.Id != args.Id));
        };
        window.ImportEffectRequested += (_, _) => imports++;
        window.ExportEffectRequested += (_, _) => exports++;
        try
        {
            window.Show();
            window.SelectPage(EffectLibraryPage.EFFECTS);
            Flush(window);
            AssertPageGeometry(window);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            Assert.True(input.IsReadOnly);
            Assert.False(UiTestActions.Find<Button>(window, "SaveEffectScriptButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(window, "DeleteEffectScriptButton").IsEffectivelyEnabled);
            Capture(window, $"effect-script-page-builtin-{width:0}x{height:0}-{language}-{(dark ? "dark" : "light")}.png");
            ClickToolbar(window, "ValidateEffectScriptButton");
            Assert.Equal(Localization.Get("Settings.ScriptValid"), window.ViewModel.Effects.ValidationStatus);
            Assert.Equal(window.ViewModel.Effects.ValidationStatus,
                ToolTip.GetTip(UiTestActions.Find<Button>(window, "ValidateEffectScriptButton")));
            AssertPageGeometry(window);
            ClickToolbar(window, "AddEffectScriptButton");
            Assert.False(input.IsReadOnly);
            Assert.True(window.ViewModel.Effects.IsDirty);
            Assert.Equal(BuiltinEffectScripts.Templates.Length, window.ViewModel.Effects.Effects.Length);
            Assert.Empty(window.ViewModel.Effects.SelectedIds);
            ClickToolbar(window, "SaveEffectScriptButton");
            Assert.Single(saved);
            Assert.False(window.ViewModel.Effects.IsDirty);
            AssertPageGeometry(window);
            Capture(window, $"effect-script-page-custom-{width:0}x{height:0}-{language}-{(dark ? "dark" : "light")}.png");
            ClickToolbar(window, "ExportEffectScriptButton");
            ClickToolbar(window, "ImportEffectScriptButton");
            Assert.Equal(1, exports);
            Assert.Equal(1, imports);
            ClickToolbar(window, "DeleteEffectScriptButton");
            Assert.Equal(saved[0].Id, Assert.Single(deleted));
            ClickToolbar(window, "AddEffectScriptButton");
            Assert.False(input.IsReadOnly);
            Assert.True(window.ViewModel.Effects.IsDirty);
            AssertPageGeometry(window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Import")]
    [InlineData("ImportFont")]
    [InlineData("IMPORT_ASS")]
    [InlineData("IMPORT_SUBTITLES")]
    public void SharedImportIconUsesLibraryKindsAndVisibleLibraryGeometry(string key)
    {
        var import = Assert.IsType<MaterialIcon>(WorkbenchIcon.Create(key, 24));
        var export = Assert.IsType<MaterialIcon>(WorkbenchIcon.Create("Export", 24));
        Assert.Equal(MaterialIconKind.Import, import.Kind);
        Assert.Equal(MaterialIconKind.Export, export.Kind);
        Assert.Same(MaterialIconDataProvider.Get<Geometry>(MaterialIconKind.Import), import.Drawing.Geometry);
        Assert.Same(MaterialIconDataProvider.Get<Geometry>(MaterialIconKind.Export), export.Drawing.Geometry);
        var icons = new[] { import, export };
        var window = new Window
        {
            Width = 120,
            Height = 80,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { import, export }
            }
        };

        try
        {
            window.Show();
            Flush(window);
            foreach (var icon in icons)
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(icon.Drawing.Geometry);
                Assert.True(geometry.Bounds.Width > 0);
                Assert.True(geometry.Bounds.Height > 0);
                Assert.Equal(new Size(24, 24), icon.Bounds.Size);
                using var target = new RenderTargetBitmap(new(24, 24), new(96, 96));
                target.Render(icon);
                using var stream = new MemoryStream();
                target.Save(stream, PngBitmapEncoderOptions.Default);
                stream.Position = 0;
                using var pixels = SKBitmap.Decode(stream);
                Assert.NotNull(pixels);
                Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 0);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(900, 420)]
    [InlineData(1000, 600)]
    public async Task ShortPreviewColumnScrollsAllParametersWithoutOverlappingStyleViewportOrTransport(double width, double height)
    {
        using var environment = new UiTestEnvironment();
        var renderer = new EffectScriptPreviewTestRenderer();
        var model = new EffectScriptLibraryViewModel();
        await model.AddCommand.ExecuteAsync(null);
        model.Source = "effect \"many-preview-parameters\" version 3\nshort-clip compress\n" +
            string.Join('\n', Enumerable.Range(0, 12).Select(index => $"var amount{index} = {index} label \"Parameter {index}\"")) +
            "\nscope whole current\nsegment all flex 1\nat 0 rotation offset(amount0)\nat 1 rotation offset(amount0)\nend\nend";
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Preview style 中文", new());
        model.UpdatePreviewStyles([style]);
        var view = new EffectScriptLibraryView(() => renderer, debounce: TimeSpan.Zero) { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Flush(window);
            var scroll = UiTestActions.Find<ScrollViewer>(window, "EffectPreviewScrollViewer");
            var panel = UiTestActions.Find<Grid>(window, "EffectPreviewPanel");
            var editor = UiTestActions.Find<EffectScriptEditor>(window, "EffectScriptEditor");
            var editorBounds = BoundsIn(editor, view);
            var parameters = UiTestActions.Find<EffectScriptParameterEditor>(window, "PreviewParameterEditor");
            var stylePicker = UiTestActions.Find<ComboBox>(window, "EffectPreviewStyleInput");
            var frame = UiTestActions.Find<Viewbox>(window, "EffectPreviewFrameViewbox");
            Assert.True(model.HasPreviewParameters);
            Assert.Equal(12, model.PreviewParameters!.Rows.Count);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.Same(scroll, Assert.Single(parameters.GetVisualAncestors().OfType<ScrollViewer>()));
            Assert.DoesNotContain(scroll, editor.GetVisualAncestors());
            Assert.InRange(Math.Abs(frame.Bounds.Width / frame.Bounds.Height - 16d / 9), 0, 0.02);
            var sections = new Control[]
            {
                UiTestActions.Find<Control>(window, "EffectPreviewTextRow"),
                UiTestActions.Find<Control>(window, "EffectPreviewDurationRow"),
                UiTestActions.Find<Control>(window, "EffectPreviewStyleRow"),
                UiTestActions.Find<Control>(window, "EffectPreviewFrameBorder"),
                UiTestActions.Find<Control>(window, "EffectPreviewTransport"),
                UiTestActions.Find<Control>(window, "EffectPreviewPositionSlider"), parameters
            };
            for (var index = 1; index < sections.Length; index++)
            {
                Assert.True(BoundsIn(sections[index - 1], panel).Bottom <= BoundsIn(sections[index], panel).Top);
            }
            frame.BringIntoView();
            Flush(window);
            var initialOffset = scroll.Offset.Y;
            window.MouseWheel(BoundsIn(frame, window).Center, new(0, -3));
            Flush(window);
            Assert.True(scroll.Offset.Y > initialOffset);
            Assert.Equal(1, UiTestActions.Find<PreviewViewportControl>(window, "EffectPreviewViewport").Zoom);
            AssertControlCanReceivePointer(window, stylePicker);
            Assert.True(stylePicker.Focus());
            UiTestActions.Press(window, Key.F4);
            Assert.True(stylePicker.IsDropDownOpen);
            UiTestActions.Press(window, Key.End);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            Assert.False(stylePicker.IsDropDownOpen);
            Assert.Equal(style.Id, Assert.IsType<SubtitleStylePreset>(stylePicker.SelectedItem).Id);
            var play = UiTestActions.Find<Control>(window, "EffectPreviewPlayPauseButton");
            AssertControlCanReceivePointer(window, play);
            var wasPlaying = model.IsPreviewPlaying;
            var playPoint = BoundsIn(play, window).Center;
            window.MouseDown(playPoint, MouseButton.Left);
            window.MouseUp(playPoint, MouseButton.Left);
            Flush(window);
            Assert.Equal(!wasPlaying, model.IsPreviewPlaying);
            AssertControlCanReceivePointer(window, UiTestActions.Find<Control>(window, "EffectPreviewLoopButton"));
            AssertControlCanReceivePointer(window, UiTestActions.Find<Control>(window, "EffectPreviewPositionSlider"));
            var last = parameters.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.Name == "EffectParameter_amount11");
            AssertControlCanReceivePointer(window, last);
            Assert.True(scroll.Offset.Y > 0);
            Assert.True(new Rect(scroll.Bounds.Size).Contains(BoundsIn(last, scroll)));
            Assert.Equal(editorBounds, BoundsIn(editor, view));
        }
        finally
        {
            await view.DisposePreviewAsync();
            window.Close();
        }
    }

    private static void AssertPageGeometry(EffectLibraryWindow window)
    {
        Flush(window);
        var page = UiTestActions.Find<EffectScriptLibraryView>(window, "EffectsView");
        var toolbar = UiTestActions.Find<WrapPanel>(window, "EffectLibraryToolbar");
        var buttons = toolbar.GetLogicalDescendants().OfType<Button>().ToArray();
        Assert.Equal(toolbarButtons, buttons.Select(button => button.Name));
        var toolbarBounds = BoundsIn(toolbar, window);
        var save = buttons[^1];
        Assert.Contains("accent", save.Classes);
        Assert.Null(window.FindControl<Button>("DuplicateEffectScriptButton"));
        foreach (var button in buttons)
        {
            var bounds = BoundsIn(button, window);
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(toolbarBounds.Contains(bounds));
            Assert.True(new Rect(window.ClientSize).Contains(bounds));
        }

        for (var first = 0; first < buttons.Length; first++)
        {
            for (var second = first + 1; second < buttons.Length; second++)
            {
                Assert.False(BoundsIn(buttons[first], window).Intersects(BoundsIn(buttons[second], window)));
            }
        }

        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "RestoreEffectScriptButton");
        var editor = UiTestActions.Find<EffectScriptEditor>(window, "EffectScriptEditor");
        var editorGrid = Assert.IsType<Grid>(editor.GetVisualParent());
        Assert.Equal(3, editorGrid.RowDefinitions.Count);
        Assert.Equal(2, Grid.GetRow(editor));
        Assert.Equal(window.ViewModel.Effects.EditorStatus, Assert.Single(editorGrid.Children.OfType<TextBlock>()).Text);
        Assert.DoesNotContain(editorGrid.Children, child => Grid.GetRow(child) > Grid.GetRow(editor));
        var editorBounds = BoundsIn(editor, page);
        var list = UiTestActions.Find<ListBox>(window, "EffectScriptList");
        Assert.InRange(Math.Abs(editorBounds.Bottom - BoundsIn(list, page).Bottom), 0, 1);
        Assert.InRange(Math.Abs(editorBounds.Bottom - page.Bounds.Height), 0, 1);
        Assert.True(editorBounds.Height >= 180);
        Assert.True(editorBounds.Width >= 180);
        var previewPanel = UiTestActions.Find<ScrollViewer>(window, "EffectPreviewScrollViewer");
        var previewBounds = BoundsIn(previewPanel, page);
        Assert.InRange(previewBounds.Width, 200, 260);
        Assert.False(previewBounds.Intersects(editorBounds));
        Assert.InRange(Math.Abs(previewBounds.Bottom - BoundsIn(list, page).Bottom), 0, 1);
        var viewport = UiTestActions.Find<PreviewViewportControl>(window, "EffectPreviewViewport");
        Assert.True(viewport.Bounds.Width > 0 && viewport.Bounds.Height >= 88);
        Assert.True(new Rect(window.ClientSize).Contains(BoundsIn(previewPanel, window)));
        AssertPreviewFitsItsViewport(window, previewPanel);
        var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
        Assert.InRange(editorBounds.Bottom - BoundsIn(input, page).Bottom, 0, 8);
        var lineNumbers = UiTestActions.Find<ScriptLineNumberMargin>(window, "ScriptLineNumbers");
        Assert.True(lineNumbers.IsEffectivelyVisible);
        Assert.True(lineNumbers.Bounds.Width > 0 && lineNumbers.Bounds.Height >= 180);
        Assert.True(new Rect(window.ClientSize).Contains(BoundsIn(lineNumbers, window)));
    }

    private static void AssertPreviewFitsItsViewport(EffectLibraryWindow window, ScrollViewer scroll)
    {
        var panel = UiTestActions.Find<Grid>(window, "EffectPreviewPanel");
        var frame = UiTestActions.Find<Border>(window, "EffectPreviewFrameBorder");
        var viewbox = UiTestActions.Find<Viewbox>(window, "EffectPreviewFrameViewbox");
        var position = UiTestActions.Find<TextBlock>(window, "EffectPreviewPositionText");
        var style = UiTestActions.Find<ComboBox>(window, "EffectPreviewStyleInput");
        Assert.InRange(panel.Bounds.Width, 1, scroll.Viewport.Width + 1);
        Assert.InRange(Math.Abs(viewbox.Bounds.Width / viewbox.Bounds.Height - 16d / 9), 0, 0.02);
        Assert.InRange(Math.Abs(frame.Bounds.Width - frame.BorderThickness.Left - frame.BorderThickness.Right - viewbox.Bounds.Width), 0, 1);
        Assert.InRange(Math.Abs(frame.Bounds.Height - frame.BorderThickness.Top - frame.BorderThickness.Bottom - viewbox.Bounds.Height), 0, 1);
        foreach (var control in new Control[] { frame, position, style })
        {
            var bounds = BoundsIn(control, scroll);
            Assert.InRange(bounds.Left, 0, scroll.Viewport.Width);
            Assert.True(bounds.Right <= scroll.Viewport.Width + 1);
        }
        var scrollbar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar =>
            ReferenceEquals(bar.TemplatedParent, scroll) && bar.Name == "PART_VerticalScrollBar");
        Assert.Equal(Orientation.Vertical, scrollbar.Orientation);
        if (scrollbar.IsVisible && scroll.Extent.Height > scroll.Viewport.Height)
        {
            var scrollbarBounds = BoundsIn(scrollbar, window);
            Assert.True(BoundsIn(frame, window).Right <= scrollbarBounds.Left + 1);
            Assert.True(BoundsIn(position, window).Right <= scrollbarBounds.Left + 1);
            Assert.True(BoundsIn(style, window).Right <= scrollbarBounds.Left + 1);
        }
    }

    private static void AssertControlCanReceivePointer(Window window, Control control)
    {
        control.BringIntoView();
        Flush(window);
        var bounds = BoundsIn(control, window);
        Assert.True(new Rect(window.ClientSize).Contains(bounds));
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(bounds.Center));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
    }

    private static void ClickToolbar(EffectLibraryWindow window, string name)
    {
        var button = UiTestActions.Find<Button>(window, name);
        Assert.Contains(UiTestActions.Find<WrapPanel>(window, "EffectLibraryToolbar"), button.GetVisualAncestors());
        Assert.True(button.IsEffectivelyEnabled);
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static Rect BoundsIn(Control control, Visual owner)
    {
        return new(control.TranslatePoint(default, owner)!.Value, control.Bounds.Size);
    }

    private static void Capture(EffectLibraryWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        Flush(window);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
