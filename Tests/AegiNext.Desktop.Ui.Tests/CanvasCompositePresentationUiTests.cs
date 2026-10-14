using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
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

public sealed class CanvasCompositePresentationUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaybackTicksInsideTheValidatedIntervalReuseActualCompositePixels(bool interactive)
    {
        using var canvas = new EffectCanvasControl
        {
            InteractivePreview = interactive,
            PlaybackActive = true
        };
        var document = new ProjectDocument { Width = 100, Height = 100 };
        var start = new MediaTime(1);
        var evaluation = interactive ? start + new MediaTime(5, 1000) : start;
        canvas.SetScene(document, null, start + new MediaTime(10, 1000));
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), evaluation, document,
            interactive, start, start + new MediaTime(1, 25));

        using (var first = Render(canvas))
        {
            Assert.Equal(SKColors.Lime, first.GetPixel(50, 50));
        }

        var sequence = canvas.PreviewSequence;
        Assert.Equal(sequence, canvas.PresentedPreviewSequence);
        Assert.True(canvas.PreviewCompletion.IsCompleted);
        for (var milliseconds = 15; milliseconds <= 35; milliseconds += 5)
        {
            canvas.SetScene(document, null, start + new MediaTime(milliseconds, 1000));
            using var tick = Render(canvas);
            Assert.Equal(SKColors.Lime, tick.GetPixel(50, 50));
            Assert.Equal(sequence, canvas.PreviewSequence);
            Assert.Equal(sequence, canvas.PresentedPreviewSequence);
        }
    }

    [AvaloniaFact]
    public void PausedInteractiveCompositeAtTheExactTargetDoesNotRecompose()
    {
        using var canvas = new EffectCanvasControl { InteractivePreview = true };
        var document = new ProjectDocument { Width = 100, Height = 100 };
        var target = new MediaTime(1015, 1000);
        canvas.SetScene(document, null, target);
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), target, document, true,
            new(1), new(26, 25));
        using var rendered = Render(canvas);
        Assert.Equal(SKColors.Lime, rendered.GetPixel(50, 50));
        Assert.Equal(canvas.PreviewSequence, canvas.PresentedPreviewSequence);
        Assert.True(canvas.PreviewCompletion.IsCompleted);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ExpiredTimeChangedDocumentOrChangedQualityCannotReuseCompositePixels(
        bool changedDocument, bool changedQuality, bool changedInteractiveTarget)
    {
        using var canvas = new EffectCanvasControl { InteractivePreview = changedInteractiveTarget };
        var document = new ProjectDocument { Width = 100, Height = 100 };
        var start = new MediaTime(1);
        canvas.SetScene(document, null, start);
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), start, document,
            changedInteractiveTarget, start, start + new MediaTime(1, 25));
        using (var initial = Render(canvas))
        {
            await DrainAsync(canvas);
        }

        if (changedQuality)
        {
            canvas.MaximumPreviewSize = new(1280, 720);
        }

        canvas.SetScene(changedDocument ? document with { Name = "New document identity" } : document,
            null, changedDocument || changedQuality ? start : changedInteractiveTarget ? start + new MediaTime(1, 100) : start + new MediaTime(1, 25));
        using (var submitted = Render(canvas))
        {
            await DrainAsync(canvas);
        }

        using var replaced = Render(canvas);
        Assert.Equal(SKColors.Red, replaced.GetPixel(50, 50));
        Assert.Equal(canvas.PreviewSequence, canvas.PresentedPreviewSequence);
    }

    [AvaloniaFact]
    public async Task EditingEndpointPoseRedrawsTheSceneInsteadOfReusingPlaybackComposite()
    {
        using var canvas = new EffectCanvasControl();
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 50, 50),
            Transform = new(25, 25),
            Fill = new(0, 0, 1),
            End = new(1)
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Layers = [layer] };
        canvas.SetScene(document, layer, layer.End, editorPose: true);
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), layer.End, document, false,
            layer.End, new(2));
        using (var submitted = Render(canvas))
        {
            await DrainAsync(canvas);
        }

        using var edited = Render(canvas);
        Assert.Equal(SKColors.Blue, edited.GetPixel(50, 40));
        Assert.Equal(canvas.PreviewSequence, canvas.PresentedPreviewSequence);
        Assert.Same(layer, document.Layers[0]);
        Assert.Equal(new MediaTime(1), document.Layers[0].End);
    }

    [AvaloniaFact]
    public async Task ActualLayerDragUsesItsDraftOverTheBackgroundAndCommitsOnlyOnRelease()
    {
        using var canvas = new EffectCanvasControl();
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 40, 40),
            Transform = new(20, 20),
            Fill = new(0, 0, 1),
            End = new(3)
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Layers = [layer] };
        canvas.SetScene(document, layer, new(1));
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), new(1), document, false,
            new(1), new(2));
        var commits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, edit) => commits.Add(edit);
        var window = new Window { Width = 100, Height = 100, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            using (var initial = Render(canvas))
            {
                Assert.Equal(SKColors.Lime, initial.GetPixel(80, 40));
            }

            window.MouseDown(new(30, 30), MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(new(70, 30));
            using (var submitted = Render(canvas))
            {
                await DrainAsync(canvas);
            }

            using var draft = Render(canvas);
            Assert.Equal(SKColors.Blue, draft.GetPixel(80, 40));
            Assert.Equal(SKColors.Red, draft.GetPixel(30, 40));
            Assert.Empty(commits);
            Assert.Same(layer, document.Layers[0]);
            Assert.Equal(new ScenePoint(20, 20), layer.Transform.Position);
            window.MouseUp(new(70, 30), MouseButton.Left);
            var commit = Assert.Single(commits);
            Assert.Equal(new ScenePoint(60, 20), commit.Transform.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ActualMaskDragRedrawsItsDraftWithoutReusingOrCommittingTheComposite()
    {
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        var subtitle = new SubtitleLine
        {
            Text = "████",
            End = new(3),
            Style = new()
            {
                FontFamily = "sans-serif",
                FontSize = 160,
                Scaling = new() { ReferenceWidth = 100, ReferenceHeight = 100 },
                Fill = new(0, 0, 1),
                StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent,
                Position = new() { Anchor = new(0, 0), Pivot = new(0, 0), Offset = new(0, 0) }
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE,
            SubtitleId = subtitle.Id,
            End = subtitle.End
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Subtitles = [subtitle], Layers = [layer] };
        ProjectValidator.Validate(document);
        using (var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory)))
        using (var scene = renderer.Render(document, new(1)))
        {
            var pixels = new Half[scene.Info.ChannelCount];
            scene.CopyPixels(pixels);
            var probe = (40 * document.Width + 40) * 4;
            Assert.True(pixels[probe + 2] > pixels[probe] + (Half)0.4,
                "The unmasked native scene must cover the draft probe with blue ink.");
        }

        canvas.SetScene(document, layer, new(1));
        canvas.PresentComposite(Frame(0, 255, 0), Frame(255, 0, 0), new(1), document, false,
            new(1), new(2));
        var commits = new List<CanvasMaskEditEventArgs>();
        canvas.MaskEdited += (_, edit) => commits.Add(edit);
        var window = new Window { Width = 100, Height = 100, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.MouseDown(new(20, 20), MouseButton.Left);
            window.MouseMove(new(60, 60));
            Assert.True(canvas.HasActiveDrag);
            using (var submitted = Render(canvas))
            {
                await DrainAsync(canvas);
            }

            using var draft = Render(canvas);
            var inside = draft.GetPixel(40, 40);
            var outside = draft.GetPixel(80, 40);
            Assert.True(inside.Blue > inside.Red + 100, $"The masked draft must show the real blue scene, got {inside}.");
            Assert.True(outside.Red > outside.Blue + 10, $"Outside the draft mask must retain the real red background, got {outside}.");
            Assert.Empty(commits);
            Assert.Null(layer.Mask);
            window.MouseUp(new(60, 60), MouseButton.Left);
            Assert.IsType<RectangleClipMask>(Assert.Single(commits).Mask);
        }
        finally
        {
            window.Close();
        }
    }

    private static SKBitmap Render(EffectCanvasControl canvas)
    {
        canvas.Measure(new(100, 100));
        canvas.Arrange(new(0, 0, 100, 100));
        using var target = new RenderTargetBitmap(new(100, 100), new(96, 96));
        using (var drawing = target.CreateDrawingContext())
        {
            canvas.Render(drawing);
        }

        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static async Task DrainAsync(EffectCanvasControl canvas)
    {
        await canvas.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background,
            TestContext.Current.CancellationToken);
    }

    private static SdrVideoFrame Frame(byte red, byte green, byte blue) => new(1, 1, [blue, green, red, 255]);
}
