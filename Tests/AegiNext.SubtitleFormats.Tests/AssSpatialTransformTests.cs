using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssSpatialTransformTests
{
    [Fact]
    public void StaticAxesUseNativeSignsAndAssLayoutDistance()
    {
        var parsed = Parse("{\\frx720\\fry-35\\frz23}Axis", 180);
        var transform = Assert.Single(parsed.Clips).Transform;

        Assert.Equal(-720, transform.RotationX);
        Assert.Equal(35, transform.RotationY);
        Assert.Equal(-23, transform.Rotation);
        Assert.Equal(625, transform.PerspectiveDistance);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
    }

    [Theory]
    [InlineData("\\frx30\\fry45\\frx\\fry")]
    [InlineData("\\frx30\\fry45\\r")]
    public void EmptyTagsAndStyleResetRestoreBothTiltAxesToZero(string tags)
    {
        var transform = Assert.Single(Parse("{" + tags + "}Axis").Clips).Transform;

        Assert.Equal(0, transform.RotationX);
        Assert.Equal(0, transform.RotationY);
    }

    [Fact]
    public void IndependentAxesPreservePowerCurvesAndOrderedOverlappingOperations()
    {
        var parsed = Parse("{\\frx20\\fry-10\\t(0,2000,2,\\frx740)\\t(0,2000,\\fry50)\\t(500,1500,\\fry90)}Axis");
        var clip = Assert.Single(parsed.Clips);
        var x = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.ROTATION_X);
        var y = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.ROTATION_Y);

        Assert.Equal(-200, SceneEvaluator.EvaluateScalarTrack(x, new(1)), 9);
        Assert.True(y.IsOrdered);
        Assert.Equal(2, y.Transforms.Length);
        Assert.Equal(-55, SceneEvaluator.EvaluateScalarTrack(y, new(1)), 9);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "Ass.UnsupportedTag");
    }

    [Fact]
    public void DifferentInlineAxesKeepAbsoluteRangeRotationsAndNeutralWholeProjection()
    {
        var clip = Assert.Single(Parse("{\\frz23\\frx30}A{\\frx60\\fry10}B").Clips);

        Assert.Equal(0, clip.Transform.RotationX);
        Assert.Equal(0, clip.Transform.RotationY);
        Assert.Equal(0, clip.Transform.Rotation);
        Assert.Equal(2, clip.Line.AnimationRanges.Length);
        Assert.All(clip.Line.AnimationRanges, range =>
        {
            Assert.Equal(SubtitleAnimationPivot.SUBTITLE_ANCHOR, range.Pivot);
            Assert.Equal(-23, range.Rotation);
            Assert.Equal(312.5, range.PerspectiveDistance);
        });
        Assert.Equal(-30, clip.Line.AnimationRanges[0].RotationX);
        Assert.Equal(-60, clip.Line.AnimationRanges[1].RotationX);
        Assert.Equal(-10, clip.Line.AnimationRanges[1].RotationY);
        Assert.DoesNotContain(clip.Tracks, track => track.Target.TextRangeId is null &&
            track.Property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y);
    }

    [Fact]
    public void FixedPositionAndScaleMapOrgIntoNativePivotWithAnimatedAxes()
    {
        var parsed = Parse("{\\pos(120,80)\\org(200,140)\\fscx150\\fscy75\\frx20\\fry30\\frz23\\t(500,1500,\\frx80\\fry60\\frz97)}Axis");
        var clip = Assert.Single(parsed.Clips);

        Assert.Equal(new ScenePoint(80, 60), clip.Transform.Position);
        Assert.Equal(new ScenePoint(80 / 1.5, 80), clip.Transform.Pivot);
        Assert.Equal(3, clip.Tracks.Length);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code is "Ass.RotationOrigin" or "Ass.UnsupportedTag");
    }

    [Fact]
    public void MovingPositionWithTiltAndOrgHasSpecificLoss()
    {
        var parsed = Parse("{\\move(120,80,260,180)\\org(200,140)\\frx20}Axis");

        Assert.Contains(parsed.Diagnostics, item => item.Code == "Ass.RotationOrigin" &&
            item.Message.Contains("perspective", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(-20, Assert.Single(parsed.Clips).Transform.RotationX);
    }

    [Fact]
    public void WholeProjectionExportsThreeAxesAndEditableAnimation()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Transform = new() { Position = new(5, 7), Pivot = new(20, 10), RotationX = 20, RotationY = -30, Rotation = 23 },
            Tracks =
            [
                new(AnimationProperty.ROTATION_X, [new(new(0), 20), new(new(2), 740)]),
                new(AnimationProperty.ROTATION_Y, [new(new(0), -30), new(new(2), -90)]),
                new(AnimationProperty.ROTATION, [new(new(0), 23), new(new(2), 97)])
            ]
        };
        var result = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var reparsed = Assert.Single(AssSubtitleFormat.Parse(result.Text, 640, 360).Clips);

        Assert.Contains("\\frx-20", result.Text, StringComparison.Ordinal);
        Assert.Contains("\\fry30", result.Text, StringComparison.Ordinal);
        Assert.Contains("\\frz-23", result.Text, StringComparison.Ordinal);
        Assert.Equal(3, reparsed.Tracks.Length);
        Assert.Equal(380, SceneEvaluator.EvaluateScalarTrack(Assert.Single(reparsed.Tracks,
            track => track.Property == AnimationProperty.ROTATION_X), new(1)), 9);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "Subtitle.Composition" or "Ass.TransformPivotAnimation" or "Ass.PerspectiveComposition");
    }

    [Fact]
    public void NativeDistanceAndProjectionTranslationReportSeparateLosses()
    {
        var line = Line();
        var layer = Layer(line) with
        {
            Transform = new() { RotationX = 30, PerspectiveDistance = 500, ProjectionTranslation = new(1, 2, 3) },
            Tracks = [new(AnimationProperty.PERSPECTIVE_DISTANCE, [new(new(0), 500), new(new(2), 800)])]
        };
        var result = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });

        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.PerspectiveDistance");
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.ProjectionTranslation");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Subtitle.Composition");
    }

    [Fact]
    public void RangeProjectionUnderWholeProjectionReportsCompositionWithoutAddingEulerAngles()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR, RotationX = 40, RotationY = 15
        };
        var line = Line() with { AnimationRanges = [range] };
        var layer = Layer(line) with { Transform = new() { RotationX = 30, RotationY = 20 } };
        var result = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });

        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.PerspectiveComposition");
        Assert.DoesNotContain("\\frx-70", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\fry-35", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void IndependentRangeProjectionRoundTripsWithoutBecomingWholeLayerRotation()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)
        {
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR, RotationX = 30, RotationY = -10, Rotation = 23
        };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 3)
        {
            Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR, RotationX = -40, RotationY = 20, Rotation = 15
        };
        var line = Line() with { AnimationRanges = [first, second] };
        var layer = Layer(line) with
        {
            Tracks = [new(new AnimationTrackTarget(AnimationProperty.ROTATION_X, TextRangeId: first.Id),
                [new(new(0), 30), new(new(2), 750)])]
        };
        var result = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var imported = Assert.Single(AssSubtitleFormat.Parse(result.Text, 640, 360).Clips);

        Assert.Equal(0, imported.Transform.RotationX);
        Assert.Equal(0, imported.Transform.RotationY);
        Assert.Equal(0, imported.Transform.Rotation);
        var firstRange = Assert.Single(imported.Line.AnimationRanges, range => range.Utf16Start == 0);
        var secondRange = Assert.Single(imported.Line.AnimationRanges, range => range.Utf16Start == 1);
        Assert.Equal(30, firstRange.RotationX);
        Assert.Equal(-10, firstRange.RotationY);
        Assert.Equal(23, firstRange.Rotation);
        Assert.Equal(-40, secondRange.RotationX);
        Assert.Equal(20, secondRange.RotationY);
        Assert.Equal(390, SceneEvaluator.EvaluateScalarTrack(Assert.Single(imported.Tracks,
            track => track.Property == AnimationProperty.ROTATION_X && track.Target.TextRangeId == firstRange.Id), new(1)), 9);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.PerspectiveComposition");
    }

    [Fact]
    public void InvalidAxisValuesLoseOnlyTheirOwnComponent()
    {
        var parsed = Parse("{\\frx1000000001\\fry35\\t(0,2000,\\frx2000000000\\fry90)}Axis");
        var clip = Assert.Single(parsed.Clips);

        Assert.Equal(0, clip.Transform.RotationX);
        Assert.Equal(-35, clip.Transform.RotationY);
        Assert.DoesNotContain(clip.Tracks, track => track.Property == AnimationProperty.ROTATION_X);
        Assert.Equal(-62.5, SceneEvaluator.EvaluateScalarTrack(Assert.Single(clip.Tracks,
            track => track.Property == AnimationProperty.ROTATION_Y), new(1)), 9);
        Assert.Contains(parsed.Diagnostics, item => item.Code is "Ass.TransformRange" or "Ass.UnsupportedTransform");
    }

    [Fact]
    public void StaticDrawingProjectionKeepsOriginalCurvesAndImportsAsNativePlane()
    {
        var path = new PathGeometry(new(1, 2), [new(new(3, 14), new(15, 16), new(17, 8))], true);
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, End = new(2), Shape = new(ShapeKind.PATH, 20, 30, path),
            Transform = new() { Position = new(100, 80), Pivot = new(5, 10), Scale = new(2, 3), RotationX = 30, RotationY = -20, Rotation = 15 }
        };
        var result = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Layers = [shape] });
        var parsed = AssSubtitleFormat.Parse(result.Text, 640, 360);

        Assert.Contains("m 1 2 b 3 14 15 16 17 8", result.Text, StringComparison.Ordinal);
        Assert.Contains("\\frx-30", result.Text, StringComparison.Ordinal);
        Assert.Contains("\\fry20", result.Text, StringComparison.Ordinal);
        Assert.True(parsed.Layers.Length == 1, string.Join("; ", parsed.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        var imported = Assert.Single(parsed.Layers);
        Assert.Equal(30, imported.Transform.RotationX);
        Assert.Equal(-20, imported.Transform.RotationY);
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code is "Ass.DrawingGeometry" or "Ass.UnsupportedTag");
    }

    [Fact]
    public void AssTextEditPreservesNativeDistanceCompensationAndUnrelatedAxisTracks()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4)
        {
            RotationX = 30, RotationY = 40, PerspectiveDistance = 500, ProjectionTranslation = new(1, 2, 3),
            Offset = new(6, 7)
        };
        var line = Line() with { AnimationRanges = [range] };
        var distance = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id),
            [new(new(0), 500), new(new(2), 800)]);
        var axis = new AnimationTrack(AnimationProperty.ROTATION_X, [new(new(0), 10), new(new(2), 730)]);
        var font = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id),
            [new(new(0), 20), new(new(2), 40)]);
        var layer = Layer(line) with { Tracks = [distance, axis, font] };
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var changed = AssTextProjection.Apply(line, source.Replace("\\fs40", "\\fs50", StringComparison.Ordinal), layer: layer);
        var restored = Assert.Single(changed.Line.AnimationRanges);

        Assert.Equal(range.PerspectiveDistance, restored.PerspectiveDistance);
        Assert.Equal(range.ProjectionTranslation, restored.ProjectionTranslation);
        Assert.Equal(range.Offset, restored.Offset);
        Assert.Same(distance, Assert.Single(changed.TextAnimationTracks!.Value, track => track.Target == distance.Target));
        Assert.Same(axis, Assert.Single(changed.TextAnimationTracks!.Value, track => track.Target == axis.Target));
    }

    private static SubtitleLine Line() => new()
    {
        Text = "Axis", End = new(2), Style = new()
        {
            Alignment = TextAlignment.TOP_LEFT,
            Position = new() { Anchor = default, Pivot = default, Offset = new(120, 80) },
            StrokeWidth = 0, ShadowOffset = default, ShadowColor = SceneColor.Transparent
        }
    };

    private static ProjectLayer Layer(SubtitleLine line) => new() { SubtitleId = line.Id, End = line.End };

    private static AssImportResult Parse(string text, int layoutHeight = 360) => AssSubtitleFormat.Parse($$"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        LayoutResX: 640
        LayoutResY: {{layoutHeight}}
        WrapStyle: 1
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Spacing, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&HFF000000,100,100,0,0,0,7
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,{{text}}
        """, 640, 360);
}
