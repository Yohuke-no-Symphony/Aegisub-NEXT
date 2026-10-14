using AegiNext.Desktop.EffectLibrary;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.EffectLibrary.Styles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsPreviewInteractionLifecycleUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageHideAndWindowCloseReleaseActivePreviewPanWithoutChangingThePreset(bool closeWindow)
    {
        using var environment = new UiTestEnvironment();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Preview", new()
        {
            FontFamily = "Embedded preview fixture", StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        }, new("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]));
        var window = new EffectLibraryWindow(new WorkbenchPreferences());
        IPointer? pointer = null;
        try
        {
            window.UpdateStyles([preset], preset.Id);
            window.SelectPage(EffectLibraryPage.STYLES);
            window.Show();
            var view = UiTestActions.Find<StyleLibraryView>(window, "StylesView");
            var viewport = UiTestActions.Find<PreviewViewportControl>(view, "StylePreviewViewport");
            var frame = UiTestActions.Find<VideoFramePresenter>(view, "StylePreviewFrame");
            viewport.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer, handledEventsToo: true);
            viewport.BringIntoView();
            await RenderAsync(window, view);
            Assert.NotNull(frame.Source);
            var point = viewport.TranslatePoint(new(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2), window)!.Value;
            Assert.Same(viewport, window.InputHitTest(point));
            window.MouseWheel(point, new(0, 2), RawInputModifiers.Meta);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(20, -10), RawInputModifiers.LeftMouseButton);
            var zoom = viewport.Zoom;
            var offset = viewport.Offset;
            Assert.Same(viewport, pointer!.Captured);
            Assert.NotEqual(default, offset);

            if (closeWindow)
            {
                window.Close();
                await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(15));
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.IsVisible);
                Assert.Null(pointer.Captured);
                Assert.Null(frame.Source);
            }
            else
            {
                window.SelectPage(EffectLibraryPage.EFFECTS);
                await window.ViewModel.NavigationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                Assert.False(view.IsVisible);
                Assert.Null(pointer.Captured);
                window.SelectPage(EffectLibraryPage.STYLES);
                await window.ViewModel.NavigationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                viewport.BringIntoView();
                await RenderAsync(window, view);
                window.MouseMove(point + new Vector(80, -20), RawInputModifiers.LeftMouseButton);
                Assert.Equal(zoom, viewport.Zoom);
                Assert.Equal(offset, viewport.Offset);
                Assert.Null(pointer.Captured);
                Assert.NotNull(frame.Source);
                window.MouseUp(point + new Vector(80, -20), MouseButton.Left);
            }
            Assert.Same(preset, window.ViewModel.Styles.Draft);
            Assert.Same(preset, Assert.Single(window.ViewModel.Styles.Styles));
            Assert.False(window.ViewModel.Styles.IsDirty);
        }
        finally
        {
            pointer?.Capture(null);
            window.Close();
        }
    }

    private static async Task RenderAsync(Window window, StyleLibraryView view)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
