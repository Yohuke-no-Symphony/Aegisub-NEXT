using AegiNext.Desktop.EffectLibrary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Styles;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsStylePreviewUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task EditingSampleRendersActualPixelsWithoutEditingTheLibrary(string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = CreateWindow(dark);
        var preset = CreatePreset();
        try
        {
            window.UpdateStyles([preset]);
            window.ViewModel.Styles.PreviewText = "Preview ABC";
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            Assert.Null(window.ViewModel.Styles.PreviewError);
            var originalPixels = Pixels(frame);
            var input = UiTestActions.Find<TextBox>(view, "StylePreviewTextInput");
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("Preview ABC 123\nSecond line");
            await RenderAsync(window, view);

            Assert.Equal("Preview ABC 123\nSecond line", window.ViewModel.Styles.PreviewText);
            Assert.Null(window.ViewModel.Styles.PreviewError);
            Assert.NotEqual(originalPixels, Pixels(frame));
            Assert.Equal(preset, window.ViewModel.Styles.Draft);
            Assert.Same(preset, Assert.Single(window.ViewModel.Styles.Styles));
            Assert.False(window.ViewModel.Styles.IsDirty);
            Assert.Null(window.ViewModel.Styles.PreviewError);
            Assert.InRange(input.Bounds.Width, 100, window.Width);
            Assert.InRange(frame.Bounds.Height, 1, 180);
            Capture(window, $"style-preview-{language}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InvalidRawFieldsKeepTheLastPixelsAndRepairRestoresRendering()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        try
        {
            window.UpdateStyles([CreatePreset()]);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            var model = window.ViewModel.Styles;
            var originalPixels = Pixels(frame);
            model.FontSizeText = "7e-";
            model.FillDraft.HexText = "#12";
            await RenderAsync(window, view);

            Assert.Equal(originalPixels, Pixels(frame));
            Assert.Equal("7e-", model.FontSizeText);
            Assert.Equal("#12", model.FillDraft.HexText);
            model.FontSizeText = "96";
            model.FillDraft.HexText = "#00FF00FF";
            await RenderAsync(window, view);

            Assert.NotEqual(originalPixels, Pixels(frame));
            Assert.True(model.FillDraft.IsDirty);
            Assert.Equal("#00FF00FF", model.FillDraft.HexText);
            Assert.Equal(SceneColor.White, model.Draft!.Style.Fill);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwitchingStylesBeforeTheUiCallbackRejectsTheOldImageAndClosingReleasesIt()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        var first = CreatePreset();
        var second = first with { Id = Guid.NewGuid(), Name = "Second", Style = first.Style with { Fill = new(0, 1, 0) } };
        try
        {
            window.UpdateStyles([first, second], first.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            window.ViewModel.Styles.PreviewText = "AAAAAAAA";
            window.ViewModel.Styles.SelectStyles(second.Id, [second.Id]);
            await RenderAsync(window, view);

            var pixels = Pixels(frame).Chunk(4).ToArray();
            Assert.Contains(pixels, pixel => pixel[1] > pixel[0] + 30 && pixel[1] > pixel[2] + 30);
            Assert.DoesNotContain(pixels, pixel => pixel[0] > 180 && pixel[1] > 180 && pixel[2] > 180);
            window.ViewModel.Styles.PreviewText = "A pending request";
            var pending = view.PreviewCompletion;
            window.Close();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Dispatcher.UIThread.RunJobs();

            Assert.Null(frame.Source);
            window.ViewModel.Styles.PreviewText = "After closing";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(frame.Source);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MultiselectAndPageVisibilityCancelRenderingAndReturningRestoresTheSample()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        var first = CreatePreset();
        var second = first with { Id = Guid.NewGuid(), Name = "Second" };
        try
        {
            window.UpdateStyles([first, second], first.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            window.ViewModel.Styles.SelectStyles(first.Id, [first.Id, second.Id]);
            await RenderAsync(window, view);
            Assert.Null(frame.Source);
            Assert.False(window.ViewModel.Styles.HasPreview);
            window.ViewModel.Styles.SelectStyles(first.Id, [first.Id]);
            window.SelectPage(EffectLibraryPage.TRANSFER);
            window.ViewModel.Styles.PreviewText = "Changed while hidden";
            await RenderAsync(window, view);
            window.SelectPage(EffectLibraryPage.STYLES);
            await RenderAsync(window, view);

            Assert.NotNull(frame.Source);
            Assert.Equal("Changed while hidden", window.ViewModel.Styles.PreviewText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwitchingToAnUnreadableEmbeddedFontClearsThePreviousStylesImage()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        var first = CreatePreset();
        var broken = first with
        {
            Id = Guid.NewGuid(), Name = "Broken",
            Font = new("Broken.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])), [1, 2, 3])
        };
        try
        {
            window.UpdateStyles([first, broken], first.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            Assert.NotNull(frame.Source);

            window.ViewModel.Styles.SelectStyles(broken.Id, [broken.Id]);
            await RenderAsync(window, view);

            Assert.NotNull(window.ViewModel.Styles.PreviewError);
            Assert.Null(frame.Source);
        }
        finally
        {
            window.Close();
        }
    }

    private static EffectLibraryWindow CreateWindow(bool dark)
    {
        return new(new WorkbenchPreferences()) { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
    }

    [AvaloniaFact]
    public async Task NavigatingThePreviewChangesRenderedGeometryWithoutCommittingTheStyleAndResetsForAnotherPreset()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        var preset = CreatePreset();
        var second = preset with { Id = Guid.NewGuid(), Name = "Second" };
        try
        {
            window.UpdateStyles([preset, second], preset.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            await RenderAsync(window, view);
            var viewport = UiTestActions.Find<PreviewViewportControl>(view, "StylePreviewViewport");
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            var pixels = Pixels(frame);
            var point = viewport.TranslatePoint(new(viewport.Bounds.Width / 2, viewport.Bounds.Height * 0.75), window)!.Value;
            var fontInput = UiTestActions.Find<NumericDraftInput>(view, "FontSizeInput");
            window.ViewModel.Styles.FontSizeText = "7e-";
            Assert.True(fontInput.FocusInput());
            viewport.BringIntoView();
            window.UpdateLayout();
            point = viewport.TranslatePoint(new(viewport.Bounds.Width / 2, viewport.Bounds.Height * 0.75), window)!.Value;
            var focus = window.FocusManager!.GetFocusedElement();
            var before = CapturePixels(window);

            window.MouseWheel(point, new(0, 3), RawInputModifiers.Meta);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(30, -50), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(30, -50), MouseButton.Left);

            Assert.True(viewport.Zoom > 1);
            Assert.False(before.SequenceEqual(CapturePixels(window)));
            Assert.Equal(pixels, Pixels(frame));
            Assert.Same(focus, window.FocusManager.GetFocusedElement());
            Assert.Equal("7e-", window.ViewModel.Styles.FontSizeText);
            Assert.Same(preset, window.ViewModel.Styles.Draft);
            var zoom = viewport.Zoom;
            var offset = viewport.Offset;
            window.ViewModel.Styles.FontSizeText = "80";
            window.ViewModel.Styles.PreviewText = "Second preview text";
            await RenderAsync(window, view);
            Assert.Equal(zoom, viewport.Zoom);
            Assert.Equal(offset, viewport.Offset);
            Capture(window, "style-preview-navigation.png");

            window.ViewModel.Styles.SelectStyles(second.Id, [second.Id]);
            await RenderAsync(window, view);
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);
            Assert.Equal(preset, window.ViewModel.Styles.Styles[0]);
        }
        finally
        {
            window.Close();
        }
    }

    private static byte[] CapturePixels(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void AResultAlreadyQueuedForTheUiCannotPresentAfterTheInputBecomesInvalid()
    {
        using var environment = new UiTestEnvironment();
        var window = CreateWindow(false);
        try
        {
            window.UpdateStyles([CreatePreset()]);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = window.FindControl<StyleLibraryView>("StylesView")!;
            Assert.True(view.PreviewCompletion.Wait(TimeSpan.FromSeconds(15), CancellationToken.None));
            var frame = view.FindControl<VideoFramePresenter>("StylePreviewFrame")!;
            Assert.Null(frame.Source);
            window.ViewModel.Styles.FontSizeText = "7e-";

            Dispatcher.UIThread.RunJobs();

            Assert.Null(frame.Source);
            Assert.Equal("7e-", window.ViewModel.Styles.FontSizeText);
        }
        finally
        {
            window.Close();
        }
    }

    private static SubtitleStylePreset CreatePreset()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        return new(Guid.NewGuid(), "Example", new()
        {
            FontFamily = "Embedded preview fixture", StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        }, new("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]));
    }

    private static async Task RenderAsync(Window window, StyleLibraryView view)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static byte[] Pixels(VideoFramePresenter frame)
    {
        var bitmap = Assert.IsType<WriteableBitmap>(frame.Source);
        var result = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        using var pixels = bitmap.Lock();
        for (var row = 0; row < bitmap.PixelSize.Height; row++)
        {
            Marshal.Copy(pixels.Address + row * pixels.RowBytes, result, row * bitmap.PixelSize.Width * 4, bitmap.PixelSize.Width * 4);
        }
        return result;
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
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
