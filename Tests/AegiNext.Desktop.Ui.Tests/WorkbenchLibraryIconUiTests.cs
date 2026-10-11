using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchLibraryIconUiTests
{
    [AvaloniaTheory]
    [InlineData("Import", MaterialIconKind.Import)]
    [InlineData("IMPORT_ASS", MaterialIconKind.Import)]
    [InlineData("MERGE_PROJECT", MaterialIconKind.CallMerge)]
    [InlineData("Export", MaterialIconKind.Export)]
    [InlineData("EXPORT_VIDEO", MaterialIconKind.Export)]
    [InlineData("ValidateScript", MaterialIconKind.FileCheckOutline)]
    [InlineData("SaveEffectAs", MaterialIconKind.ContentSaveEditOutline)]
    [InlineData("AddPathPoint", MaterialIconKind.VectorPointPlus)]
    [InlineData("RemovePathPoint", MaterialIconKind.VectorPointMinus)]
    [InlineData("ResetPosition", MaterialIconKind.CrosshairsGps)]
    [InlineData("ResetColors", MaterialIconKind.FormatColorReset)]
    [InlineData("VIEW_LOG", MaterialIconKind.TextBoxOutline)]
    [InlineData("VIEW_PREVIEW", MaterialIconKind.Monitor)]
    public void SemanticKeysCreateTheLibraryControlAndItsCachedGeometry(string key, MaterialIconKind kind)
    {
        var icon = Assert.IsType<MaterialIcon>(WorkbenchIcon.Create(key, 18));
        Assert.Equal(kind, icon.Kind);
        Assert.Equal(18, icon.Width);
        Assert.Equal(18, icon.Height);
        Assert.Same(MaterialIconDataProvider.Get<Geometry>(kind), icon.Drawing.Geometry);
        Assert.True(icon.Drawing.Geometry!.Bounds.Width > 0 && icon.Drawing.Geometry.Bounds.Height > 0);
    }

    [AvaloniaTheory]
    [InlineData("Import", MaterialIconKind.Import)]
    [InlineData("Export", MaterialIconKind.Export)]
    [InlineData("Play", MaterialIconKind.Play)]
    [InlineData("Settings", MaterialIconKind.Cog)]
    [InlineData("MERGE_PROJECT", MaterialIconKind.CallMerge)]
    public void NativeMenuBitmapMatchesTheSameLibrarySilhouette(string key, MaterialIconKind kind)
    {
        var geometry = new MaterialIcon { Kind = kind }.Drawing.Geometry!;
        using var native = WorkbenchIcon.CreateNative(key);
        Assert.Equal(new PixelSize(24, 24), native.PixelSize);
        using var stream = new MemoryStream();
        native.Save(stream, PngBitmapEncoderOptions.Default);
        using var pixels = SKBitmap.Decode(stream.ToArray());
        var checkedPixels = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                var inside = geometry.FillContains(new(x + 0.5, y + 0.5));
                var corners = new[] { new Point(x + 0.1, y + 0.1), new Point(x + 0.9, y + 0.1),
                    new Point(x + 0.1, y + 0.9), new Point(x + 0.9, y + 0.9) };
                if (corners.Any(point => geometry.FillContains(point) != inside))
                {
                    continue;
                }

                var alpha = pixels.GetPixel(x, y).Alpha;
                Assert.True(inside ? alpha >= 240 : alpha <= 15, $"{key}: unexpected alpha {alpha} at {x},{y}");
                checkedPixels++;
            }
        }
        Assert.True(checkedPixels > 200);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LibraryIconsRenderInBothThemesAndIconTextRetainsItsControlAcrossKeyChanges(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var keys = new[] { "File", "Open", "Save", "SaveAs", "Import", "Export", "Settings", "Add", "Delete",
            "Undo", "Redo", "Play", "Pause", "Loop", "Backward", "Forward", "Enter", "ExitTiming", "Split",
            "Merge", "Style", "Effects", "Subtitles", "SubtitleEditor", "Timeline", "Magnet", "Spectrum",
            "Waveform", "Keyframe", "Path", "Mask", "Group", "Bold", "Italic", "Volume", "Mute", "Keyboard",
            "ValidateScript", "SaveEffectAs", "AddPathPoint", "RemovePathPoint", "ResetPosition", "ResetColors",
            "ManageEffectScripts", "ApplySelectionPreset", "ClearSelectionStyle", "VIEW_LOG" };
        var content = new WrapPanel();
        foreach (var key in keys)
        {
            content.Children.Add(new Border
            {
                Width = 145, Height = 55, Padding = new(8),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { WorkbenchIcon.Create(key, 24), new TextBlock { Text = key, FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center } }
                }
            });
        }
        var label = new IconText { IconKey = "Import", Text = "导入 Import 123" };
        var window = new Window
        {
            Width = 920, Height = 560, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Margin = new(12), Spacing = 8, Children = { content, label } }
        };
        try
        {
            window.Show();
            Flush(window);
            var icons = window.GetVisualDescendants().OfType<MaterialIcon>().ToArray();
            Assert.Equal(keys.Length + 1, icons.Length);
            Assert.All(icons, icon =>
            {
                Assert.NotNull(icon.Template);
                var path = Assert.Single(icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                Assert.Same(icon.Drawing.Geometry, path.Data);
                Assert.Equal(icon.Foreground, path.Fill);
                Assert.True(path.Bounds.Width > 0 && path.Bounds.Height > 0);
            });
            var retained = label.GetVisualDescendants().OfType<MaterialIcon>().Single();
            label.IconKey = "Export";
            label.Text = "导出 Export 456";
            Flush(window);
            Assert.Same(retained, label.GetVisualDescendants().OfType<MaterialIcon>().Single());
            Assert.Equal(MaterialIconKind.Export, retained.Kind);
            Capture(window, $"material-icons-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
