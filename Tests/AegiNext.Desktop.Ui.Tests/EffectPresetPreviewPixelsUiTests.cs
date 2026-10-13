using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.EffectPresets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>通过实际预设工作者和卡片位图验证退场像素，避免仅验证模拟渲染器的播放位置。</summary>
public sealed class EffectPresetPreviewPixelsUiTests
{
    /// <summary>单次悬停完成退场及循环，呈现位图在退场末尾发生变化，离开后恢复中点海报。</summary>
    [AvaloniaTheory]
    [InlineData("fade-out")]
    [InlineData("pop-out")]
    [InlineData("slide-out")]
    [InlineData("fade-in-out")]
    public async Task RealCardPixelsChangeDuringTheOutroAndRestoreThePosterOnLeave(string presetId)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.Session.EffectScripts.Completion;
        await context.Session.Fonts.EnsureLoadedAsync();
        var source = context.Session.DocumentSnapshot;
        var playhead = context.ViewModel.Preview.Position;
        await using var view = new EffectPresetsPanelView(context.ViewModel.EffectPresets, context.Session);
        var window = new Window { Width = 520, Height = 700, Content = view };
        try
        {
            window.Show();
            Flush(window);
            var card = view.GetVisualDescendants().OfType<EffectPresetCardView>()
                .Single(candidate => candidate.ViewModel.Id == presetId);
            card.BringIntoView();
            Flush(window);
            await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Flush(window);
            Assert.Equal(new MediaTime(3, 2), card.PresentedPosition);
            var presenter = card.GetVisualDescendants().OfType<VideoFramePresenter>().Single();
            var poster = PixelSignal(presenter);
            Assert.True(poster > 0);
            window.FocusManager.Focus(null);
            var pointer = card.TranslatePoint(new(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
            window.MouseMove(pointer);
            Flush(window);
            var request = card.Request;
            var elapsed = Stopwatch.StartNew();
            var samples = new List<(MediaTime Position, long Signal)>();
            var minimumOutro = poster;
            var previous = MediaTime.Zero;
            var looped = false;
            while (!looped && elapsed.Elapsed < TimeSpan.FromSeconds(6))
            {
                Flush(window);
                Assert.Same(request, card.Request);
                Assert.Equal(presetId, view.ActivePreviewId);
                if (card.IsPreviewAnimating && card.PresentedPosition is { } position)
                {
                    var signal = PixelSignal(presenter);
                    samples.Add((position, signal));
                    if (position >= new MediaTime(11, 4))
                    {
                        minimumOutro = Math.Min(minimumOutro, signal);
                    }
                    looped = previous > new MediaTime(11, 4) && position < new MediaTime(3, 10);
                    previous = position;
                }
                await Task.Delay(15, TestContext.Current.CancellationToken);
            }

            Assert.True(looped, $"The real preview did not loop: {string.Join(", ", samples)}");
            Assert.True(minimumOutro < poster * 0.8,
                $"The presented pixels did not show the outro. Poster={poster}; samples={string.Join(", ", samples)}");
            window.MouseMove(new(2, 2));
            Flush(window);
            await view.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Flush(window);
            Assert.Null(view.ActivePreviewId);
            Assert.False(card.IsPreviewAnimating);
            Assert.Equal(new MediaTime(3, 2), card.PresentedPosition);
            Assert.Equal(poster, PixelSignal(presenter));
            Assert.Same(source, context.Session.DocumentSnapshot);
            Assert.Equal(playhead, context.ViewModel.Preview.Position);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    private static long PixelSignal(VideoFramePresenter presenter)
    {
        using var stream = new MemoryStream();
        Assert.IsType<Bitmap>(presenter.Source, exactMatch: false).Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        return pixels.Pixels.Sum(pixel => (long)Math.Abs(pixel.Red - 64) + Math.Abs(pixel.Green - 64) + Math.Abs(pixel.Blue - 64));
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
    }
}
