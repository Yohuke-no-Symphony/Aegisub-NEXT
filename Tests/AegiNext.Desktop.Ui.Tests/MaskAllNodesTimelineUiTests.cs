using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskAllNodesTimelineUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public void AllAnimatedNodesAndHandlesRemainVisibleAndStationaryAcrossPointSelection(string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var third = new MaskNode { Position = new(50, 60) };
        var staticNode = new MaskNode { Position = new(70, 80) };
        var cue = new SubtitleLine { End = new(8), Text = "全部节点 All points 123" };
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_IN_HANDLE, second.Id);
        var thirdTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, third.Id);
        var layer = Layer(cue) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }, new() { Nodes = [third, staticNode] }] },
            Tracks = [new(thirdTarget, [new(new(1), third.Position), new(new(3), new ScenePoint(80, 90))]),
                new(secondTarget, [new(new(1), new ScenePoint(2, 4)), new(new(3), new ScenePoint(6, 8))]),
                new(firstTarget, [new(new(1), first.Position), new(new(3), new ScenePoint(20, 30))])]
        };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 70, SelectedMaskNodeId = first.Id };
        timeline.SetDocument(document, cue.Id, layer);
        var window = new Window { Width = 800, Height = 400, Content = timeline, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Flush(window);
            Assert.Equal(new[] { AnimationProperty.MASK_NODE_POSITION, AnimationProperty.MASK_NODE_IN_HANDLE }, timeline.GetAnimationProperties(layer.Id));
            Assert.Equal(new[] { firstTarget, thirdTarget, secondTarget }, timeline.GetAnimationTargets(layer.Id));
            Assert.DoesNotContain(timeline.KeyframeMarkers, marker => marker.Identity.Target.NodeId == staticNode.Id);
            Assert.Equal(new[] { first.Id, second.Id, third.Id }.Order(), timeline.KeyframeMarkers.Select(marker => marker.Identity.Target.NodeId!.Value).Distinct().Order());
            var positions = timeline.KeyframeMarkers.ToDictionary(marker => (marker.Identity, marker.Components), marker => marker.Position);
            var clip = timeline.GetClipRectangle(layer.Id);
            var point = timeline.GetKeyframePoint(layer.Id, firstTarget, new(1), first.Position)!.Value;
            var thirdPoint = timeline.GetKeyframePoint(layer.Id, thirdTarget, new(1), third.Position)!.Value;
            Assert.NotEqual(timeline.KeyframeMarkers.First(marker => marker.Identity.Target == firstTarget).Curve,
                timeline.KeyframeMarkers.First(marker => marker.Identity.Target == thirdTarget).Curve);
            Assert.True(point.Y > thirdPoint.Y);
            using var selected = Capture(timeline);
            foreach (var id in new Guid?[] { second.Id, third.Id, null, first.Id })
            {
                timeline.SelectedMaskNodeId = id;
                Flush(window);
                Assert.Equal(clip, timeline.GetClipRectangle(layer.Id));
                Assert.Equal(positions.Count, timeline.KeyframeMarkers.Count);
                Assert.All(timeline.KeyframeMarkers, marker => Assert.Equal(positions[(marker.Identity, marker.Components)], marker.Position));
            }
            timeline.SelectedMaskNodeId = second.Id;
            using var otherSelected = Capture(timeline);
            Assert.False(Pixels(selected, point).SequenceEqual(Pixels(otherSelected, point)));
            timeline.ToggleTrackCollapse(layer.TrackId);
            Assert.Empty(timeline.GetAnimationTargets(layer.Id));
            timeline.ToggleTrackCollapse(layer.TrackId);
            Assert.Equal(new[] { firstTarget, thirdTarget, secondTarget }, timeline.GetAnimationTargets(layer.Id));
            if (Environment.GetEnvironmentVariable("AEGINEXT_MASK_NODE_CAPTURE_DIRECTORY") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = otherSelected.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(directory, $"all-mask-nodes-{language}-{(dark ? "dark" : "light")}.png"));
                image.SaveTo(file);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DifferentClipsReuseBlockSpaceAndAnUnselectedNodesDragChangesOnlyItsOwnStableTarget()
    {
        using var environment = new UiTestEnvironment();
        var firstCue = new SubtitleLine { End = new(4) };
        var secondCue = new SubtitleLine { Start = new(5), End = new(9) };
        var first = AnimatedLayer(firstCue);
        var second = AnimatedLayer(secondCue);
        var target = second.Tracks[1].Target;
        var key = second.Tracks[1].Keyframes[0];
        var editor = new ProjectEditor(new() { Subtitles = [firstCue, secondCue], Layers = [first, second] });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 65, IsSnapEnabled = false, SelectedMaskNodeId = first.Tracks[0].Target.NodeId };
        timeline.SetDocument(editor.Snapshot, firstCue.Id, first);
        var commits = 0;
        timeline.KeyframeSelected += (_, e) =>
        {
            Assert.Equal(target, e.Target);
            timeline.SelectedMaskNodeId = target.NodeId;
            timeline.EffectTarget = target;
            timeline.SetDocument(editor.Snapshot, secondCue.Id, second);
            e.SelectionAccepted = true;
        };
        timeline.KeyframeMoved += (_, e) =>
        {
            commits++;
            Assert.Equal(target, e.Target);
            Assert.Equal(key.Value, e.NewValue);
            editor.UpdateLayer(second.Id, layer => layer with
            {
                Tracks = layer.Tracks.Select(track => track.Target == target ? track with
                {
                    Keyframes = [key with { Time = e.NewTime }]
                } : track).ToImmutableArray()
            });
        };
        var window = new Window { Width = 850, Height = 380, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            Assert.Equal(2, timeline.GetAnimationTargets(first.Id).Count);
            Assert.Equal(2, timeline.GetAnimationTargets(second.Id).Count);
            Assert.Equal(AnimationProperty.MASK_NODE_POSITION, Assert.Single(timeline.GetAnimationProperties(first.Id)));
            var firstPoint = timeline.GetKeyframePoint(first.Id, first.Tracks[1].Target, new(1), key.Value)!.Value;
            var point = timeline.GetKeyframePoint(second.Id, target, new(1), key.Value)!.Value;
            Assert.Equal(firstPoint.Y, point.Y, 8);
            var originalPositions = timeline.KeyframeMarkers.ToDictionary(marker => (marker.Identity, marker.Components), marker => marker.Position);
            window.MouseDown(point, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            Assert.All(timeline.KeyframeMarkers, marker => Assert.Equal(originalPositions[(marker.Identity, marker.Components)], marker.Position));
            window.MouseMove(point + new Vector(65, -55));
            Assert.Equal(0, commits);
            window.MouseUp(point + new Vector(65, -55), MouseButton.Left);
            Assert.Equal(1, commits);
            var changed = editor.Snapshot.Layers[1];
            Assert.Equal(new MediaTime(2), changed.Tracks[1].Keyframes[0].Time);
            Assert.Same(second.Tracks[0], changed.Tracks[0]);
            Assert.Same(first, editor.Snapshot.Layers[0]);
            Assert.True(editor.Undo());
            Assert.Same(second, editor.Snapshot.Layers[1]);
            Assert.True(editor.Redo());
            Assert.Equal(new MediaTime(2), editor.Snapshot.Layers[1].Tracks[1].Keyframes[0].Time);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CoincidentKeysInDifferentNodeBlocksRemainIndependentlySelectable()
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { End = new(4) };
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = first.Position };
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id);
        var layer = Layer(cue) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks = [new(firstTarget, [new(new(1), first.Position)]), new(secondTarget, [new(new(1), second.Position)])]
        };
        using var timeline = new SubtitleTimelineControl();
        timeline.PixelsPerSecond = 70;
        timeline.SelectedMaskNodeId = second.Id;
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        AnimationTrackTarget? selected = null;
        timeline.KeyframeSelected += (_, e) => selected = e.Target;
        var window = new Window { Width = 800, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var point = timeline.GetKeyframePoint(layer.Id, secondTarget, new(1), second.Position)!.Value;
            Assert.NotEqual(point, timeline.GetKeyframePoint(layer.Id, firstTarget, new(1), first.Position));
            timeline.SelectedMaskNodeId = first.Id;
            window.MouseMove(point);
            Assert.Equal(secondTarget, timeline.HoveredKeyframe!.Identity.Target);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(secondTarget, selected);
            Assert.Equal(2, timeline.KeyframeMarkers.Select(marker => marker.Identity.Target).Distinct().Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TenThousandAnimatedPointsKeepIndependentCompactBlocksAndOnlyVisibleMarkers()
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { End = new(4) };
        var nodes = Enumerable.Range(0, 10000).Select(index => new MaskNode { Position = new(index, index + 10) }).ToImmutableArray();
        var layer = Layer(cue) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = nodes }] },
            Tracks = nodes.Select(node => new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id),
                [new(new(1), node.Position)])).ToImmutableArray()
        };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        ProjectValidator.Validate(document);
        using var timeline = new SubtitleTimelineControl();
        timeline.PixelsPerSecond = 70;
        timeline.SetDocument(document, cue.Id, layer);
        var window = new Window { Width = 400, Height = 220, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            Assert.Equal(AnimationProperty.MASK_NODE_POSITION, Assert.Single(timeline.GetAnimationProperties(layer.Id)));
            Assert.Equal(10000, timeline.GetAnimationTargets(layer.Id).Count);
            Assert.InRange(timeline.KeyframeMarkers.Count, 1, 6);
            Assert.All(timeline.KeyframeMarkers, marker =>
                Assert.Equal(40, timeline.GetEffectClipRectangle(new(layer.Id, marker.Identity.Target))!.Value.Height));
            Assert.True(timeline.ContentHeight >= 10000 * 40);
            Assert.True(timeline.GetClipRectangle(layer.Id)!.Value.Bottom > window.Bounds.Height);
            var source = timeline.GetClipRectangle(layer.Id);
            timeline.SelectedMaskNodeId = nodes[^1].Id;
            Assert.Equal(source, timeline.GetClipRectangle(layer.Id));
            Assert.Equal(10000, timeline.GetAnimationTargets(layer.Id).Count);
            var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodes[0].Id);
            var firstBlock = timeline.GetEffectClipRectangle(new(layer.Id, firstTarget))!.Value;
            timeline.SetViewport(timeline.Viewport with { VerticalOffset = firstBlock.Top - timeline.RulerHeight }, 4);
            Flush(window);
            Assert.InRange(timeline.KeyframeMarkers.Count, 1, 6);
            var marker = Assert.Single(timeline.KeyframeMarkers, value => value.Identity.Target == firstTarget);
            window.MouseMove(marker.Position);
            Assert.Equal(firstTarget, timeline.HoveredKeyframe!.Identity.Target);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer AnimatedLayer(SubtitleLine cue)
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        return Layer(cue) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), [new(new(1), first.Position)]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id), [new(new(1), second.Position)])]
        };
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new() { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };

    private static IEnumerable<SKColor> Pixels(SKBitmap bitmap, Point point) => Enumerable.Range(-9, 19)
        .SelectMany(y => Enumerable.Range(-9, 19).Select(x => bitmap.GetPixel((int)point.X + x, (int)point.Y + y)));

    private static SKBitmap Capture(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        bitmap.Render(control);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
