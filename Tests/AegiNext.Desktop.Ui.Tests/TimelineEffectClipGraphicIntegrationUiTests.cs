using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineEffectClipGraphicIntegrationUiTests
{
    [AvaloniaTheory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task GraphicBlockSelectionKeepsItsCompleteTargetAndPointerPlacementUsesTheParentTrack(LayerKind kind)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/effect-block.png");
        var track = new ProjectTrack { Name = "Graphics" };
        var layer = GraphicLayer(kind, asset.Id) with { TrackId = track.Id };
        var document = new ProjectDocument { Tracks = [track], Assets = [asset], Layers = [layer] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        var id = new TimelineEffectClipId(layer.Id, layer.Tracks[0].Target);
        var block = timeline.GetEffectClipRectangle(id)!.Value;
        var local = new Point(block.Left + 36, block.Top + 10);
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));

        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);
        context.Window.MouseMove(point);

        Assert.Equal(layer.Id, context.Session.SelectedLayerId);
        Assert.Equal(kind, context.Session.SelectedLayer!.Kind);
        Assert.Equal(id, context.ViewModel.Timeline.SelectedEffectClipId);
        Assert.Equal(id.Target, context.Session.SceneEditing.Target);
        Assert.Null(context.Session.SelectedCueId);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Same(document, context.Session.DocumentSnapshot);
        var paste = Assert.IsType<TimelineClipContextEventArgs>(timeline.GetClipPasteTarget());
        var placement = Assert.IsType<TimelineClipContextEventArgs>(timeline.GetClipPlacementTarget(local));
        Assert.Equal(track.Id, paste.TrackId);
        Assert.Equal(track.Id, placement.TrackId);
        Assert.Equal(paste.Time, placement.Time);
        Assert.True(paste.Time >= layer.Start && paste.Time <= layer.End);
    }

    [AvaloniaTheory]
    [InlineData(LayerKind.IMAGE, false)]
    [InlineData(LayerKind.IMAGE, true)]
    [InlineData(LayerKind.SHAPE, false)]
    [InlineData(LayerKind.SHAPE, true)]
    public void ParentLeftTrimKeepsBlockAndKeyframePreviewAlignedAndRetainsTheImagePlaybackClock(LayerKind kind, bool stretch)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/effect-block.png");
        var layer = GraphicLayer(kind, asset.Id);
        var document = new ProjectDocument { Assets = [asset], Layers = [layer] };
        var editor = new ProjectEditor(document);
        var id = new TimelineEffectClipId(layer.Id, layer.Tracks[0].Target);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60, IsSnapEnabled = false, IsStepEnabled = false };
        timeline.SetDocument(document, null, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [id] };
        var commits = 0;
        timeline.TimingChanged += (_, e) =>
        {
            commits++;
            Assert.Equal(stretch ? TimelineEditMode.STRETCH : TimelineEditMode.CROP, e.Mode);
            Assert.False(e.IsMove);
            editor.MoveClip(e.Id, e.TrackId!.Value, e.Start, e.End, e.Mode, false);
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers[0]);
        };
        var window = new Window { Width = 850, Height = 320, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var origin = new Point(clip.Left + 2, clip.Center.Y);
            var destination = origin + new Vector(60, 0);
            var modifiers = stretch ? RawInputModifiers.Control : RawInputModifiers.None;
            Assert.Same(timeline, window.InputHitTest(origin));
            window.MouseDown(origin, MouseButton.Left, modifiers);
            window.MouseMove(destination, modifiers);

            Assert.True(timeline.HasActiveDrag);
            Assert.Same(document, editor.Snapshot);
            var previewBlock = timeline.GetEffectClipRectangle(id)!.Value;
            var previewClip = timeline.GetClipRectangle(layer.Id)!.Value;
            Assert.Equal(clip.Left + 60, previewBlock.Left, 6);
            Assert.Equal(clip.Right, previewBlock.Right, 6);
            Assert.Equal(previewClip.Left, previewBlock.Left);
            Assert.Equal(previewClip.Width, previewBlock.Width);
            var marker = Assert.Single(timeline.KeyframeMarkers, value => value.Identity.LayerId == layer.Id && value.Value.Scalar == 0.25);
            Assert.Equal(new MediaTime(stretch ? 3 : 1, stretch ? 4 : 1), marker.Identity.Time);
            Assert.Equal(timeline.HeaderWidth + (stretch ? 3.75 : 3) * 60, marker.Position.X, 6);

            window.MouseUp(destination, MouseButton.Left, modifiers);
            Flush(window);

            var changed = Assert.Single(editor.Snapshot.Layers);
            Assert.Equal(new MediaTime(3), changed.Start);
            Assert.Equal(new MediaTime(6), changed.End);
            Assert.Equal(new MediaTime(stretch ? 0 : 1), changed.AnimationOffset);
            Assert.False(timeline.IsEffectClipCollapsed(id));
            Assert.Equal(previewBlock, timeline.GetEffectClipRectangle(id));
            Assert.Equal(marker.Position, timeline.GetKeyframePoint(layer.Id, id.Target, marker.Identity.Time, marker.Value));
            if (kind == LayerKind.IMAGE)
            {
                Assert.Equal(new MediaTime(stretch ? 3 : 5), changed.Image!.Playback!.SourceOffset);
                Assert.Equal(2, changed.Image.Playback.Speed);
                Assert.Equal(layer.Image!.Crop, changed.Image.Crop);
            }
            else
            {
                Assert.Same(layer.Shape, changed.Shape);
            }
            Assert.Equal(1, commits);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortAndPartiallyVisibleBlocksKeepAnAccessibleExpansionEntry(bool shortBlock)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(1),
            End = shortBlock ? new(11, 10) : new(5),
            Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.5)])]
        };
        var document = new ProjectDocument { Layers = [layer] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        context.ViewModel.Timeline.ViewStart = shortBlock ? 0 : 2;
        Flush(context.Window);
        var id = new TimelineEffectClipId(layer.Id, layer.Tracks[0].Target);
        var block = timeline.GetEffectClipRectangle(id)!.Value;
        if (shortBlock)
        {
            Assert.True(block.Width < 24);
            Assert.Null(timeline.GetEffectClipExpanderRectangle(id));
            var point = timeline.TranslatePoint(new(block.Center.X, block.Top + 10), context.Window)!.Value;
            Assert.Same(timeline, context.Window.InputHitTest(point));
            context.Window.MouseDown(point, MouseButton.Right);
            context.Window.MouseUp(point, MouseButton.Right);
            Flush(context.Window);
            var panel = Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());
            try
            {
                Assert.True(panel.AnimationMenu.IsOpen);
                var item = TimelineTrackTestActions.Item(panel.AnimationMenu, "ToggleEffectClipCollapseMenuItem");
                Assert.True(item.Command!.CanExecute(null));
                item.Command.Execute(null);
            }
            finally
            {
                panel.AnimationMenu.Close();
            }
        }
        else
        {
            Assert.True(block.Left < timeline.HeaderWidth);
            var expander = timeline.GetEffectClipExpanderRectangle(id)!.Value;
            Assert.True(expander.Left >= timeline.HeaderWidth);
            var point = timeline.TranslatePoint(expander.Center, context.Window)!.Value;
            Assert.Same(timeline, context.Window.InputHitTest(point));
            context.Window.MouseDown(point, MouseButton.Left);
            context.Window.MouseUp(point, MouseButton.Left);
        }
        Flush(context.Window);
        Assert.False(timeline.IsEffectClipCollapsed(id));
        Assert.Equal(76, timeline.GetEffectClipRectangle(id)!.Value.Height);
        Assert.Equal(id, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void ExpandedAndCollapsedBlocksReuseStaticDrawingDuringPlaybackAndLiveTiming()
    {
        using var environment = new UiTestEnvironment();
        var cues = Enumerable.Range(0, 64).Select(index => new SubtitleLine
        {
            Start = new(index * 5), End = new(index * 5 + 4), Text = $"Effects {index}"
        }).ToArray();
        var layers = cues.Select(cue => new ProjectLayer
        {
            Id = cue.Id, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25), new(new(3), 0.75)]),
                new(AnimationProperty.ROTATION, [new(new(1), 20), new(new(3), 40)])]
        }).ToArray();
        var document = new ProjectDocument { Subtitles = [.. cues], Layers = [.. layers] };
        var layer = layers[0];
        var expanded = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 60, IsWaveformVisible = false, IsSpectrumVisible = false
        };
        timeline.SetDocument(document, layer.SubtitleId, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [expanded] };
        var window = new Window { Width = 850, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            Capture(timeline);
            Assert.True(timeline.CurveSampleCount > 0);
            Assert.False(timeline.IsEffectClipCollapsed(expanded));
            Assert.True(timeline.IsEffectClipCollapsed(new(layer.Id, new(AnimationProperty.ROTATION))));
            var builds = timeline.StaticDrawingBuildCount;
            var samples = timeline.CurveSampleCount;
            var layouts = timeline.TextLayoutBuildCount;
            var indexes = timeline.VisibleClipIndexBuildCount;
            for (var frame = 1; frame <= 8; frame++)
            {
                timeline.Position = new(frame, 4);
                Capture(timeline);
            }
            Assert.Equal(builds, timeline.StaticDrawingBuildCount);
            Assert.Equal(samples, timeline.CurveSampleCount);
            Assert.Equal(layouts, timeline.TextLayoutBuildCount);
            Assert.Equal(indexes, timeline.VisibleClipIndexBuildCount);

            timeline.SetTimingPreview(new(layer.SubtitleId!.Value, layer.Start, layer.End));
            Capture(timeline);
            builds = timeline.StaticDrawingBuildCount;
            var ranges = timeline.RangeProjectionBuildCount;
            var originalBlock = timeline.GetEffectClipRectangle(expanded)!.Value;
            for (var frame = 1; frame <= 8; frame++)
            {
                timeline.SetTimingPreview(new(layer.SubtitleId.Value, layer.Start, layer.End + new MediaTime(frame, 20)));
                Capture(timeline);
                Assert.Equal(timeline.GetClipRectangle(layer.Id)!.Value.Right, timeline.GetEffectClipRectangle(expanded)!.Value.Right);
            }
            Assert.Equal(builds, timeline.StaticDrawingBuildCount);
            Assert.Equal(ranges, timeline.RangeProjectionBuildCount);
            Assert.Equal(indexes, timeline.VisibleClipIndexBuildCount);
            Assert.True(timeline.GetEffectClipRectangle(expanded)!.Value.Right > originalBlock.Right);
            timeline.SetTimingPreview(null);
            Assert.Equal(originalBlock, timeline.GetEffectClipRectangle(expanded));
            Assert.Same(layer, document.Layers[0]);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer GraphicLayer(LayerKind kind, Guid assetId)
    {
        return new()
        {
            Kind = kind, Start = new(2), End = new(6),
            Image = kind == LayerKind.IMAGE ? new(assetId, 200, 100)
            {
                Crop = new(0.1, 0.2, 0.5, 0.5), Playback = new() { SourceOffset = new(3), Speed = 2 }
            } : null,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.ARROW, 200, 100) : null,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25), new(new(3), 0.75)])]
        };
    }

    private static SubtitleTimelineControl Prepare(MainWindowTestContext context)
    {
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        context.ViewModel.Timeline.Viewport = new(0, 60);
        Flush(context.Window);
        return timeline;
    }

    private static void Capture(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        bitmap.Render(control);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
