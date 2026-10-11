using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectCanvasPresentationUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoEditingShowsTheProjectAnchorForTransformedSubtitleClips(bool transformed)
    {
        using var canvas = new EffectCanvasControl();
        var cue = new SubtitleLine
        {
            Text = "ABC", End = new(2),
            Style = new()
            {
                FontFamily = "sans-serif", FontSize = 22,
                Position = new() { Anchor = new(0.25, 0.25), Pivot = new(0.5, 0.5), Offset = new(50, 0) }
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End,
            Transform = transformed ? new(40, 10, ScaleX: 2, ScaleY: 2) : new()
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Subtitles = [cue], Layers = [layer] };
        ProjectValidator.Validate(document);
        canvas.SetScene(document, layer, MediaTime.Zero);
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        try
        {
            using var pixels = await CaptureAsync(window, canvas);
            var board = canvas.ProjectRectangle;
            var anchor = new Point(100, 100);
            var x = (int)Math.Round(board.X + anchor.X / document.Width * board.Width);
            var y = (int)Math.Round(board.Y + anchor.Y / document.Height * board.Height);
            var marker = pixels.GetPixel(x, y);
            Assert.True(marker.Red > 200 && marker.Green > 150 && marker.Blue < 80,
                $"The gold anchor marker must use the project anchor coordinates, got {marker} at {x},{y}.");
            Assert.Same(cue, document.Subtitles[0]);
            Assert.Equal(new ScenePoint(50, 0), cue.Style.Position!.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderingRecoveryNotificationStopsWhenTheCanvasIsDisposed(bool disposeBeforeNotification)
    {
        using var environment = new UiTestEnvironment();
        using var canvas = new EffectCanvasControl();
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing.png");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 40, 20) };
        var document = new ProjectDocument { Width = 400, Height = 400, Assets = [asset], Layers = [layer] };
        var failures = 0;
        var recoveries = 0;
        canvas.RenderingFailed += (_, _) => failures++;
        canvas.RenderingRecovered += (_, _) => recoveries++;
        canvas.Measure(new(400, 400));
        canvas.Arrange(new(0, 0, 400, 400));
        canvas.SetScene(document, layer, MediaTime.Zero, environment.DirectoryPath);
        using var target = new RenderTargetBitmap(new(400, 400), new(96, 96));
        using (var drawing = target.CreateDrawingContext())
        {
            canvas.Render(drawing);
        }

        await WaitForPreviewAsync(canvas);
        Assert.Equal(1, failures);
        canvas.SetScene(document with { Layers = [] }, null, MediaTime.Zero, environment.DirectoryPath);
        using (var drawing = target.CreateDrawingContext())
        {
            canvas.Render(drawing);
        }

        Assert.Equal(0, recoveries);
        if (disposeBeforeNotification)
        {
            canvas.Dispose();
        }

        await WaitForPreviewAsync(canvas);
        Assert.Equal(disposeBeforeNotification ? 0 : 1, recoveries);
    }

    [AvaloniaFact]
    public async Task MissingFontInGeometryAndRenderingReportsOnceAndKeepsVideoVisible()
    {
        using var environment = new UiTestEnvironment();
        using var canvas = new EffectCanvasControl();
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "missing.ttf");
        var cue = new SubtitleLine { Text = "Missing font", End = new(3), Style = new() { FontAssetId = font.Id } };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End };
        var document = new ProjectDocument { Width = 400, Height = 400, Assets = [font], Subtitles = [cue], Layers = [layer] };
        ProjectValidator.Validate(document);
        var failures = new List<CanvasRenderingFailedEventArgs>();
        canvas.RenderingFailed += (_, e) =>
        {
            canvas.InvalidateVisual();
            failures.Add(e);
        };
        canvas.SetScene(document, layer, new(0), environment.DirectoryPath);
        canvas.PresentVideo(Frame(1, 1, 255, 0, 0));
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        try
        {
            using (var initial = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Red, initial.GetPixel(212, 212));
            }

            var point = new Point(150, 150);
            Assert.Same(canvas, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(canvas.HasActiveDrag);
            using (var pixels = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Red, pixels.GetPixel(212, 212));
            }

            var error = Assert.Single(failures);
            Assert.IsType<FileNotFoundException>(error.Error);
            Assert.Equal(document.Id, error.ProjectId);
            Assert.Equal(MediaTime.Zero, error.Position);
            canvas.PresentVideo(Frame(1, 1, 0, 255, 0));
            using (var updated = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Lime, updated.GetPixel(212, 212));
            }

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Single(failures);
            var repairedCue = cue with { Style = cue.Style with { FontAssetId = null } };
            canvas.SetScene(document with { Subtitles = [repairedCue] }, layer, new(0), environment.DirectoryPath);
            using var repaired = await CaptureAsync(window, canvas);
            Assert.Single(failures);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CorruptImageReportsDecodeFailureWithoutLosingFallbackOrCommittingAGesture()
    {
        using var environment = new UiTestEnvironment();
        File.WriteAllText(Path.Combine(environment.DirectoryPath, "broken.png"), "This is not an encoded image.");
        using var canvas = new EffectCanvasControl();
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "broken.png");
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Image = new(asset.Id, 40, 20), Transform = new(100, 100)
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Assets = [asset], Layers = [layer] };
        ProjectValidator.Validate(document);
        var failures = new List<CanvasRenderingFailedEventArgs>();
        var commits = new List<CanvasLayerEditEventArgs>();
        canvas.RenderingFailed += (_, e) => failures.Add(e);
        canvas.LayerEdited += (_, e) => commits.Add(e);
        canvas.SetScene(document, layer, new(0), environment.DirectoryPath);
        canvas.PresentVideo(Frame(1, 1, 255, 0, 0));
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        try
        {
            using (var pixels = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Red, pixels.GetPixel(212, 212));
            }

            var failure = Assert.IsType<ProjectImageRenderException>(Assert.Single(failures).Error);
            Assert.Equal(asset.Id, failure.AssetId);
            Assert.IsType<InvalidDataException>(failure.InnerException);
            var point = new Point(112, 112);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(20, 0));
            using var failedDraft = await CaptureAsync(window, canvas);
            Assert.False(canvas.HasActiveDrag);
            window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
            Assert.Empty(commits);
            Assert.Single(failures);
            canvas.ClearVideo();
            using var cleared = await CaptureAsync(window, canvas);
            Assert.Equal(SKColors.Black, cleared.GetPixel(212, 212));
            Assert.Single(failures);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task VideoPreviewReportsActualImageFailureThroughViewModelToSession()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        await context.OpenMediaAsync();
        var canvas = context.Window.Panels["preview"].FindControl<EffectCanvasControl>("EffectCanvas")!;
        var observed = new List<CanvasRenderingFailedEventArgs>();
        canvas.RenderingFailed += (_, e) => observed.Add(e);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "invalid-canvas.png");
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        File.WriteAllText(Path.Combine(context.Session.ProjectDirectory, asset.RelativePath), "Invalid PNG payload.");
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 40, 20) };
        context.Session.Editor.Apply("Add invalid image fixture", document => document with
        {
            Assets = document.Assets.Add(asset), Layers = document.Layers.Add(layer)
        });
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        context.Window.GetCommand(WorkbenchCommand.VIEW_PREVIEW).Execute(null);
        using var captured = await CaptureAsync(context.Window, canvas);
        var failure = Assert.Single(observed);
        var imageFailure = Assert.IsType<ProjectImageRenderException>(failure.Error);
        Assert.Equal(asset.Id, imageFailure.AssetId);
        Assert.IsType<InvalidDataException>(imageFailure.InnerException);
        Assert.Same(failure.Error, context.Session.LastError);
        Assert.Equal(failure.Error.Message, context.ViewModel.Error);
        Assert.True(canvas.HasPresentation);
    }

    [AvaloniaFact]
    public async Task ProjectBoardShowsAspectPreservedVideoAndActualSceneThenReleasesOnClear()
    {
        using var canvas = new EffectCanvasControl();
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 40),
            Transform = new(100, 100), Fill = new(0, 0, 1)
        };
        canvas.SetScene(new() { Width = 400, Height = 400, Layers = [layer] }, null, new(0));
        canvas.PresentVideo(Frame(2, 1, 255, 0, 0));
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        try
        {
            using (var pixels = await CaptureAsync(window, canvas))
            {
                Assert.Equal(new Rect(0, 0, 424, 424), canvas.ProjectRectangle);
                Assert.Equal(SKColors.Black, pixels.GetPixel(5, 5));
                Assert.Equal(SKColors.Black, pixels.GetPixel(212, 50));
                Assert.Equal(SKColors.Red, pixels.GetPixel(50, 212));
                var scenePixel = pixels.GetPixel(150, 125);
                Assert.True(scenePixel.Blue > scenePixel.Red + 100, "The real shape must be visible over the decoded video.");
            }

            Assert.True(canvas.HasVideo);
            Assert.True(canvas.HasPresentation);
            canvas.ClearVideo();
            Assert.False(canvas.HasVideo);
            Assert.False(canvas.HasPresentation);
            using var cleared = await CaptureAsync(window, canvas);
            Assert.Equal(SKColors.Black, cleared.GetPixel(50, 212));
            Assert.True(cleared.GetPixel(150, 125).Blue > 100);
            canvas.Dispose();
            Assert.False(canvas.HasPresentation);
            Assert.Throws<ObjectDisposedException>(() => canvas.PresentVideo(Frame(1, 1, 0, 255, 0)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PointerDragPreservesTheRotatedScaledClipAndCommitsOnceAtRelease()
    {
        using var canvas = new EffectCanvasControl();
        var child = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Transform = new(80, 140, ScaleX: 2, ScaleY: 2, Rotation: 90)
        };
        canvas.SetScene(new() { Width = 600, Height = 400, Layers = [child] }, child, new(0));
        var commits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => commits.Add(e);
        var window = new Window { Width = 624, Height = 424, Content = canvas };
        window.Show();
        try
        {
            using var pixels = await CaptureAsync(window, canvas);
            var board = canvas.ProjectRectangle;
            var scale = board.Width / 600;
            var origin = new Point(board.X + 60 * scale, board.Y + 180 * scale);
            var delta = new Vector(40 * scale, 0);
            Assert.Same(canvas, window.InputHitTest(origin));
            window.MouseDown(origin, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(origin + delta);
            Assert.Empty(commits);
            window.MouseUp(origin + delta, MouseButton.Left);
            var edit = Assert.Single(commits);
            Assert.Equal(child.Id, edit.LayerId);
            Assert.Equal(120, edit.Transform.X, 5);
            Assert.Equal(140, edit.Transform.Y, 5);
            Assert.Equal(child.Transform with { X = edit.Transform.X, Y = edit.Transform.Y }, edit.Transform);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EndpointEditingPoseIsVisibleButNormalHalfOpenPlaybackRemainsEmpty()
    {
        using var canvas = new EffectCanvasControl();
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 40),
            End = new(2), Transform = new(100, 100), Fill = new(0, 0, 1),
            Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(100, 100)), new(new(2), new ScenePoint(120, 100))])]
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Layers = [layer] };
        canvas.SetScene(document, layer, layer.End, editorPose: true);
        var window = new Window { Width = 400, Height = 400, Content = canvas };
        window.Show();
        try
        {
            using (var editing = await CaptureAsync(window, canvas))
            {
                Assert.True(editing.GetPixel(160, 120).Blue > 200);
            }
            canvas.SetScene(document, layer, layer.End);
            using var playback = await CaptureAsync(window, canvas);
            Assert.Equal(SKColors.Black, playback.GetPixel(160, 120));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwitchingLayersAtTheSameEndpointRefreshesTheEditingPoseAndItsPresentationIdentity()
    {
        using var canvas = new EffectCanvasControl();
        var first = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 40),
            End = new(2), Transform = new(100, 100), Fill = new(0, 0, 1)
        };
        var secondTrack = new ProjectTrack { Name = "Second shape" };
        var second = first with { Id = Guid.NewGuid(), TrackId = secondTrack.Id, Transform = new(240, 100), Fill = new(1, 0, 0) };
        var document = new ProjectDocument
        {
            Width = 400, Height = 400, Tracks = [ProjectTrack.Default, secondTrack], Layers = [first, second]
        };
        canvas.SetScene(document, first, first.End, editorPose: true);
        var window = new Window { Width = 400, Height = 400, Content = canvas };
        window.Show();
        try
        {
            using (var initial = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Blue, initial.GetPixel(160, 120));
                Assert.Equal(SKColors.Black, initial.GetPixel(280, 120));
            }
            var firstSequence = canvas.PresentedPreviewSequence;

            canvas.SetScene(document, second, second.End, editorPose: true);
            using (var switched = await CaptureAsync(window, canvas))
            {
                Assert.Equal(SKColors.Black, switched.GetPixel(160, 120));
                Assert.Equal(SKColors.Red, switched.GetPixel(280, 120));
            }
            Assert.True(canvas.PresentedPreviewSequence > firstSequence);
            Assert.Equal(canvas.PreviewSequence, canvas.PresentedPreviewSequence);
            var secondSequence = canvas.PresentedPreviewSequence;

            canvas.SetScene(document, first, first.End, editorPose: true);
            using var restored = await CaptureAsync(window, canvas);
            Assert.Equal(SKColors.Blue, restored.GetPixel(160, 120));
            Assert.Equal(SKColors.Black, restored.GetPixel(280, 120));
            Assert.True(canvas.PresentedPreviewSequence > secondSequence);
            Assert.Same(first, document.Layers[0]);
            Assert.Same(second, document.Layers[1]);
            Assert.Equal(new MediaTime(2), first.End);
            Assert.Equal(new MediaTime(2), second.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BitmapReplacementClearAndDisposalRejectLatePresentation()
    {
        using var surface = new VideoFrameSurface();
        surface.Present(Frame(2, 1, 255, 0, 0));
        var first = surface.Bitmap!;
        surface.Present(Frame(2, 1, 0, 255, 0));
        Assert.Same(first, surface.Bitmap);
        surface.Present(Frame(1, 2, 0, 0, 255));
        Assert.NotSame(first, surface.Bitmap);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            using var framebuffer = first.Lock();
        });
        surface.Clear();
        Assert.Null(surface.Bitmap);
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.Present(Frame(1, 1, 0, 0, 0)));
        Assert.Null(surface.Bitmap);
    }

    private static async Task<SKBitmap> CaptureAsync(Window window, EffectCanvasControl canvas)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            Assert.True(DateTime.UtcNow < deadline, "Canvas did not present the latest submitted preview sequence.");
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using (var submitted = window.CaptureRenderedFrame())
            {
                Assert.NotNull(submitted);
            }

            var sequence = canvas.PreviewSequence;
            await WaitForPreviewAsync(canvas);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (canvas.PreviewSequence != sequence || canvas.PresentedPreviewSequence != sequence)
            {
                await Task.Yield();
                continue;
            }

            using var stream = new MemoryStream();
            frame.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
    }

    private static async Task WaitForPreviewAsync(EffectCanvasControl canvas)
    {
        await canvas.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(static () =>
        {
        }, DispatcherPriority.Background,
            TestContext.Current.CancellationToken);
    }

    private static SdrVideoFrame Frame(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }

        return new(width, height, pixels);
    }
}
