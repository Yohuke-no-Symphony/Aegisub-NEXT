using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssMaskPlacementTests
{
    [Fact]
    public void ImportedRectangleUsesCenterPlacementAndKeepsItsScreenGeometry()
    {
        var parser = new AssMaskParser(new(2), 1, 1, 640, 360);
        parser.Apply("clip", "(10,20,50,80)", Guid.NewGuid(), 0, 0);
        var mask = Assert.IsType<RectangleClipMask>(parser.Mask);

        Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Anchor);
        Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Pivot);
        Assert.Equal(new MaskReferenceFrame { Origin = new(10, 20), Size = new(40, 60) }, mask.ReferenceFrame);
        Assert.Equal(new ScenePoint(-290, -130), mask.Placement.Offset);
        var matrix = ClipMaskPlacementGeometry.CreateMatrix(mask, new(640, 360));
        Assert.Equal(new ScenePoint(10, 20), matrix.Map(mask.TopLeft));
        Assert.Equal(new ScenePoint(50, 80), matrix.Map(mask.BottomRight));
    }

    [Fact]
    public void RectangleAnimationKeepsTheImportedReferenceFrameAtEveryTime()
    {
        var parser = new AssMaskParser(new(2), 1, 1, 640, 360);
        var id = Guid.NewGuid();
        parser.Apply("clip", "(10,20,50,80)", id, 0, 0);
        Assert.True(parser.TryTransform("(0,2000,1,\\clip(30,40,90,120))", id, 0, 0));
        var mask = Assert.IsType<RectangleClipMask>(parser.Mask);
        var layer = new ProjectLayer { Mask = mask, Tracks = parser.Tracks() };

        foreach (var time in new MediaTime[] { new(0), new(1), new(2) })
        {
            var evaluated = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, time));
            var matrix = ClipMaskPlacementGeometry.CreateMatrix(evaluated, new(640, 360));
            Assert.Equal(mask.ReferenceFrame, evaluated.ReferenceFrame);
            Assert.Equal(evaluated.TopLeft, matrix.Map(evaluated.TopLeft));
            Assert.Equal(evaluated.BottomRight, matrix.Map(evaluated.BottomRight));
        }
    }

    [Fact]
    public void ImportedBezierReferenceFrameIncludesCurveExtrema()
    {
        var parser = new AssMaskParser(new(2), 1, 1, 640, 360);
        parser.Apply("clip", "(m 0 0 b 0 100 40 100 40 0)", Guid.NewGuid(), 0, 0);
        var mask = Assert.IsType<VectorClipMask>(parser.Mask);

        Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Anchor);
        Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Pivot);
        Assert.Equal(new ScenePoint(40, 75), mask.ReferenceFrame.Size);
        Assert.Equal(new ScenePoint(-300, -142.5), mask.Placement.Offset);
        var matrix = ClipMaskPlacementGeometry.CreateMatrix(mask, new(640, 360));
        Assert.Equal(new ScenePoint(0, 0), matrix.Map(mask.Contours[0].Nodes[0].Position));
        Assert.Equal(new ScenePoint(40, 0), matrix.Map(mask.Contours[0].Nodes[1].Position));
    }

    [Fact]
    public void AdvancedSourceAndNativeRectangleAnimationIncludeStaticPlacement()
    {
        var line = new SubtitleLine { Text = "x", End = new(1) };
        var mask = new RectangleClipMask
        {
            BottomRight = new(40, 60), ReferenceFrame = new() { Size = new(40, 60) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(10, -5) },
            Transform = new() { Position = new(7, 3) }
        };
        var layer = Layer(line, mask) with
        {
            Tracks =
            [
                new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(20, 10))]),
                new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT, [new(new(0), new ScenePoint(40, 60)), new(new(1), new ScenePoint(60, 70))])
            ]
        };
        var projection = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360, layer: layer);

        Assert.Contains("\\clip(317,148,357,208)", projection.Source, StringComparison.Ordinal);
        Assert.Contains("\\t(0,1000,1,\\clip(337,158,377,218))", projection.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(projection.Diagnostics, diagnostic => diagnostic.Code == "Ass.NativeMaskAnimation");
        var written = AssSubtitleFormat.Write(Document(line, layer));
        Assert.Contains("\\clip(317,148,357,208)", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\t(0,1000,1,\\clip(337,158,377,218))", written.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
    }

    [Fact]
    public void SourceEditRecoversLocalGeometryAndTracksWithoutChangingPlacementOrReferenceFrame()
    {
        var line = new SubtitleLine { Text = "x", End = new(1) };
        var mask = new RectangleClipMask
        {
            TopLeft = new(10, 10), BottomRight = new(50, 70), ReferenceFrame = new() { Origin = new(10, 10), Size = new(40, 60) },
            Placement = new() { Anchor = new(0.25, 0.75), Pivot = new(0.25, 0.5), Offset = new(12, -20) },
            Transform = new() { Position = new(3, 7), Scale = new(2, 0.5) }
        };
        var layer = Layer(line, mask);
        var projection = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360, layer: layer);
        Assert.Contains("\\clip(155,242,235,272)", projection.Source, StringComparison.Ordinal);
        var source = projection.Source.Replace("\\clip(155,242,235,272)",
            "\\clip(175,247,255,282)\\t(0,1000,1,\\clip(195,252,275,287))", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source, canvasWidth: 640, canvasHeight: 360, layer: layer);
        var actual = Assert.IsType<RectangleClipMask>(edited.Mask);

        Assert.Equal(mask.Placement, actual.Placement);
        Assert.Equal(mask.ReferenceFrame, actual.ReferenceFrame);
        Assert.Equal(mask.Transform, actual.Transform);
        Assert.Equal(new ScenePoint(20, 20), actual.TopLeft);
        Assert.Equal(new ScenePoint(60, 90), actual.BottomRight);
        var track = Assert.Single(edited.MaskTracks, track => track.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        Assert.Equal(new ScenePoint(20, 20), SceneEvaluator.EvaluateTrack(track, new(0)).Vector);
        Assert.Equal(new ScenePoint(30, 30), SceneEvaluator.EvaluateTrack(track, new(1)).Vector);
    }

    [Fact]
    public void SampledMaskAnimationIncludesTheSameStaticPlacementAtEveryFrame()
    {
        var line = new SubtitleLine { Text = "x", End = new(1) };
        var mask = new RectangleClipMask
        {
            BottomRight = new(40, 60), ReferenceFrame = new() { Size = new(40, 60) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) }
        };
        var layer = Layer(line, mask) with
        {
            Tracks = [new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(1), new ScenePoint(20, 10))])]
        };
        var written = AssSubtitleFormat.Write(Document(line, layer) with { FrameRate = new(2, 1) });

        Assert.Contains("\\clip(300,150,340,210)", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\clip(310,155,350,215)", written.Text, StringComparison.Ordinal);
        Assert.Equal(2, written.Text.Split('\n').Count(value => value.StartsWith("Dialogue:", StringComparison.Ordinal)));
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
    }

    [Fact]
    public void EditingMirroredBezierGeometryPreservesNodeIdentitiesAndInverseHandleVectors()
    {
        var first = new MaskNode { Position = new(0, 0), OutHandle = new(10, 20) };
        var second = new MaskNode { Position = new(40, 60), InHandle = new(-10, -20) };
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [first, second] }], ReferenceFrame = new() { Size = new(40, 60) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(0, 0) },
            Transform = new() { Scale = new(-2, 0.5) }
        };
        var line = new SubtitleLine { Text = "x" };
        var layer = Layer(line, mask);
        var source = "{\\clip(m 360 165 b 320 170 300 185 280 195)}x";
        var edited = AssTextProjection.Apply(line, source, canvasWidth: 640, canvasHeight: 360, layer: layer);
        var actual = Assert.IsType<VectorClipMask>(edited.Mask);
        var contour = Assert.Single(actual.Contours);

        Assert.Equal(mask.Placement, actual.Placement);
        Assert.Equal(mask.ReferenceFrame, actual.ReferenceFrame);
        Assert.Equal(mask.Contours[0].Id, contour.Id);
        Assert.Equal(first.Id, contour.Nodes[0].Id);
        Assert.Equal(second.Id, contour.Nodes[1].Id);
        Assert.Equal(new ScenePoint(20, 10), contour.Nodes[0].OutHandle);
        Assert.Equal(new ScenePoint(-10, -20), contour.Nodes[1].InHandle);
        Assert.Equal(new ScenePoint(40, 60), contour.Nodes[1].Position);
    }

    [Fact]
    public void ZeroScaleSourceEditStillRejectsAnUnrecoverableInverse()
    {
        var line = new SubtitleLine { Text = "x" };
        var mask = new RectangleClipMask
        {
            BottomRight = new(40, 60), ReferenceFrame = new() { Size = new(40, 60) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) },
            Transform = new() { Scale = new(0, 1) }
        };
        var layer = Layer(line, mask);
        var error = Assert.Throws<InvalidOperationException>(() =>
            AssTextProjection.Apply(line, "{\\clip(10,20,30,40)}x", canvasWidth: 640, canvasHeight: 360, layer: layer));

        Assert.Contains("zero scaling", error.Message, StringComparison.Ordinal);
    }

    private static ProjectLayer Layer(SubtitleLine line, ClipMask mask) => new()
    {
        Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End, Mask = mask
    };

    private static ProjectDocument Document(SubtitleLine line, ProjectLayer layer) => new()
    {
        Width = 640, Height = 360, Subtitles = [line], Layers = [layer]
    };
}
