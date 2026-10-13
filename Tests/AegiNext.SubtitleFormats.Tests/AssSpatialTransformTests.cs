using System.Collections.Immutable;
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
    [InlineData(180, null, 312.5)]
    [InlineData(720, null, 312.5)]
    [InlineData(360, 180, 625)]
    [InlineData(360, 720, 156.25)]
    [InlineData(180, 360, 312.5)]
    public void CameraUsesCompleteLayoutResolutionOrTargetCanvasStorage(int playResHeight, int? layoutHeight, double expected)
    {
        var parsed = Parse("{\\pos(120,80)\\frx30\\fry20}Axis", layoutHeight, playResHeight);

        Assert.Equal(expected, Assert.Single(parsed.Clips).Transform.PerspectiveDistance);
    }

    [Fact]
    public void IncompleteLayoutResolutionUsesTargetCanvasCamera()
    {
        var source = "[Script Info]\nPlayResX: 640\nPlayResY: 180\nLayoutResY: 180\n" +
            "[Events]\nFormat: Layer, Start, End, Text\nDialogue: 0,0:00:00.00,0:00:02.00,{\\frx30}Axis";

        Assert.Equal(312.5, Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Clips).Transform.PerspectiveDistance);
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
        var rotation = Assert.Single(clip.Tracks);

        Assert.Equal(AnimationProperty.ROTATION_3D, rotation.Property);
        Assert.True(rotation.IsOrdered);
        Assert.Equal(3, rotation.Transforms.Length);
        Assert.Equal(new SceneVector3(-200, -55, 0), SceneEvaluator.EvaluateTrack(rotation, new(1)).Vector3);
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
            track.Property == AnimationProperty.ROTATION_3D);
    }

    [Fact]
    public void FixedPositionAndScaleMapOrgIntoNativePivotWithAnimatedAxes()
    {
        var parsed = Parse("{\\pos(120,80)\\org(200,140)\\fscx150\\fscy75\\frx20\\fry30\\frz23\\t(500,1500,\\frx80\\fry60\\frz97)}Axis");
        var clip = Assert.Single(parsed.Clips);

        Assert.Equal(new ScenePoint(80, 60), clip.Transform.Position);
        Assert.Equal(new ScenePoint(80 / 1.5, 80), clip.Transform.Pivot);
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(clip.Tracks).Property);
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
        Assert.Equal(380, SceneEvaluator.EvaluateTrack(Assert.Single(reparsed.Tracks,
            track => track.Property == AnimationProperty.ROTATION_3D), new(1)).Vector3.X, 9);
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
        Assert.Equal(390, SceneEvaluator.EvaluateTrack(Assert.Single(imported.Tracks,
            track => track.Property == AnimationProperty.ROTATION_3D && track.Target.TextRangeId == firstRange.Id), new(1)).Vector3.X, 9);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.PerspectiveComposition");
    }

    [Fact]
    public void InvalidAxisValuesLoseOnlyTheirOwnComponent()
    {
        var parsed = Parse("{\\frx1000000001\\fry35\\t(0,2000,\\frx2000000000\\fry90)}Axis");
        var clip = Assert.Single(parsed.Clips);

        Assert.Equal(0, clip.Transform.RotationX);
        Assert.Equal(-35, clip.Transform.RotationY);
        Assert.Equal(new SceneVector3(0, -62.5, 0), SceneEvaluator.EvaluateTrack(Assert.Single(clip.Tracks,
            track => track.Property == AnimationProperty.ROTATION_3D), new(1)).Vector3);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssTextEditPreservesNativeDistanceCompensationAndUnrelatedAxisTracks(bool rangeRotation)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4)
        {
            RotationX = 30, RotationY = 40, PerspectiveDistance = 500, ProjectionTranslation = new(1, 2, 3),
            Offset = new(6, 7)
        };
        var line = Line() with { AnimationRanges = [range] };
        var distance = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id),
            [new(new(0), 500), new(new(2), 800)]);
        var axis = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.ROTATION_3D, TextRangeId: rangeRotation ? range.Id : null),
            [new(new(0), AnimationValue.FromVector3(new(10, 0, 0))), new(new(2), AnimationValue.FromVector3(new(730, 0, 0)))]);
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

    [Theory]
    [InlineData(10001)]
    [InlineData(30000)]
    public void AssTextEditPreservesCanonicalOrderedRotationAtItsFullBudget(int count)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        var line = Line() with { AnimationRanges = [range] };
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(default),
            Transforms = Enumerable.Range(0, count).Select(index => new AnimationTransformOperation(Guid.NewGuid(),
                new(index, count), new(index + 1, count), AnimationValue.FromVector3(new(index % 13, index % 17, index % 19))))
                .ToImmutableArray()
        };
        var font = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id),
            [new(new(0), 20), new(new(2), 40)]);
        var layer = Layer(line) with { Tracks = [rotation, font] };
        var source = AssTextProjection.Create(line, layer: layer).Source;

        var changed = AssTextProjection.Apply(line, source.Replace("\\fs40", "\\fs50", StringComparison.Ordinal), layer: layer);

        Assert.Same(rotation, Assert.Single(changed.TextAnimationTracks!.Value, track => track.Target == rotation.Target));
        Assert.Equal(count, rotation.Transforms.Length);
    }

    [Fact]
    public void CanonicalVector3ExportsIndependentMasksAndPowerOperationsWithoutLoss()
    {
        var line = Line();
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(new(20, -10, 23)),
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(2), AnimationValue.FromVector3(new(740, -10, 23)), 2) { ComponentMask = 1 },
                new(Guid.NewGuid(), new(0), new(2), AnimationValue.FromVector3(new(20, 50, 23))) { ComponentMask = 2 },
                new(Guid.NewGuid(), new(1, 2), new(3, 2), AnimationValue.FromVector3(new(20, 90, 23))) { ComponentMask = 2 }
            ]
        };
        var layer = Layer(line) with { Tracks = [rotation] };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var imported = Assert.Single(AssSubtitleFormat.Parse(exported.Text, 640, 360).Clips);
        var converted = Assert.Single(imported.Tracks);

        Assert.Equal(AnimationProperty.ROTATION_3D, converted.Property);
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.TransformCurveApproximation");
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1, 2), new(1), new(3, 2), new(2) })
        {
            AssertRotationEqual(SceneEvaluator.EvaluateTrack(rotation, time).Vector3, SceneEvaluator.EvaluateTrack(converted, time).Vector3);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void CanonicalTimeUnionRecoversAnUnsplitPowerCurveForAssExport(int duration)
    {
        var line = Line();
        line = line with { End = new(duration) };
        var phase = 1d / duration;
        var x = new AnimationTrack(AnimationProperty.ROTATION_X,
            [new(new(0), 20, KeyframeInterpolation.POWER) { Exponent = 2 }, new(new(duration), 740)]);
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D,
        [
            new(new(0), AnimationValue.FromVector3(new(20, -30, 23)), KeyframeInterpolation.POWER)
            {
                Exponent = 2, CurveEnd = phase,
                ComponentCurves = [new(KeyframeInterpolation.LINEAR), new(KeyframeInterpolation.HOLD)]
            },
            new(new(1), AnimationValue.FromVector3(new(SceneEvaluator.EvaluateScalarTrack(x, new(1)), -30 - 60 * phase, 23)), KeyframeInterpolation.POWER)
            {
                Exponent = 2, CurveStart = phase,
                ComponentCurves = [new(KeyframeInterpolation.LINEAR), new(KeyframeInterpolation.HOLD)]
            },
            new(new(duration), AnimationValue.FromVector3(new(740, -90, 23)))
        ]);
        var layer = Layer(line) with { Tracks = [rotation] };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var converted = Assert.Single(Assert.Single(AssSubtitleFormat.Parse(exported.Text, 640, 360).Clips).Tracks);

        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.TransformCurveApproximation");
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1, 2), new(1), new(3, 2), new(2) })
        {
            AssertRotationEqual(SceneEvaluator.EvaluateTrack(rotation, time).Vector3, SceneEvaluator.EvaluateTrack(converted, time).Vector3);
        }
    }

    [Fact]
    public void CanonicalPhaseChangesBeyondMachineRoundingRemainExplicitlyLossy()
    {
        var line = Line() with { End = new(3) };
        var phase = 1d / 3 + 1e-10;
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D,
        [
            new(new(0), AnimationValue.FromVector3(default), KeyframeInterpolation.POWER)
            {
                Exponent = 2, CurveEnd = phase
            },
            new(new(1), AnimationValue.FromVector3(new(80, 0, 0)), KeyframeInterpolation.POWER)
            {
                Exponent = 2, CurveStart = phase
            },
            new(new(3), AnimationValue.FromVector3(new(720, 0, 0)))
        ]);
        var layer = Layer(line) with { Tracks = [rotation] };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });

        Assert.Contains(exported.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation");
    }

    [Theory]
    [InlineData(KeyframeInterpolation.LINEAR, 7, false)]
    [InlineData(KeyframeInterpolation.POWER, 7, false)]
    [InlineData(KeyframeInterpolation.LINEAR, 0, false)]
    [InlineData(KeyframeInterpolation.POWER, 0, false)]
    [InlineData(KeyframeInterpolation.EASE_OUT, 7, true)]
    public void OrderedVector3CurveOverridesAccelerationOrReportsSpecificApproximation(KeyframeInterpolation interpolation,
        double acceleration, bool approximate)
    {
        var line = Line();
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D, [])
        {
            InitialValue = AnimationValue.FromVector3(default),
            Transforms = [new(Guid.NewGuid(), new(0), new(2), AnimationValue.FromVector3(new(100, 0, 0)), acceleration)
            {
                ComponentMask = 1, Curve = new(interpolation) { Exponent = 2 }
            }]
        };
        var layer = Layer(line) with { Tracks = [rotation] };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var converted = Assert.Single(Assert.Single(AssSubtitleFormat.Parse(exported.Text, 640, 360).Clips).Tracks);

        Assert.Equal(approximate, exported.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.TransformCurveApproximation"));
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
        if (!approximate)
        {
            AssertRotationEqual(SceneEvaluator.EvaluateTrack(rotation, new(1)).Vector3, SceneEvaluator.EvaluateTrack(converted, new(1)).Vector3);
        }
    }

    [Theory]
    [InlineData(KeyframeInterpolation.LINEAR, false)]
    [InlineData(KeyframeInterpolation.POWER, false)]
    [InlineData(KeyframeInterpolation.EASE_OUT, true)]
    public void RangeVector3OrderedCurveIsPreservedOrExplicitlySampled(KeyframeInterpolation interpolation, bool sampled)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4) { Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR };
        var line = Line() with { AnimationRanges = [range] };
        var rotation = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.ROTATION_3D, TextRangeId: range.Id), [])
        {
            InitialValue = AnimationValue.FromVector3(new(20, -10, 23)),
            Transforms = [new(Guid.NewGuid(), new(0), new(2), AnimationValue.FromVector3(new(100, -10, 23)), 7)
            {
                ComponentMask = 1, Curve = new(interpolation) { Exponent = 2 }
            }]
        };
        var layer = Layer(line) with { Tracks = [rotation] };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });
        var imported = Assert.Single(AssSubtitleFormat.Parse(exported.Text, 640, 360).Clips);
        var converted = Assert.Single(imported.Tracks, track => track.Property == AnimationProperty.ROTATION_3D);

        Assert.Equal(sampled, exported.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.AnimationSampling"));
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code is "Subtitle.Composition" or "Ass.PerspectiveComposition");
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1, 2), new(1), new(3, 2), new(1999, 1000) })
        {
            var expected = SceneEvaluator.EvaluateTrack(rotation, time).Vector3;
            var actual = SceneEvaluator.EvaluateTrack(converted, time).Vector3;
            Assert.InRange(Math.Abs(expected.X - actual.X), 0, sampled ? 1d / 255 : 1e-9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
    }

    [Fact]
    public void CanonicalPureZWithMovementKeepsTheExistingTwoDimensionalOriginPath()
    {
        var line = Line();
        var rotation = new AnimationTrack(AnimationProperty.ROTATION_3D,
            [new(new(0), AnimationValue.FromVector3(new(0, 0, 30))), new(new(2), AnimationValue.FromVector3(new(0, 0, 30)))]);
        var layer = Layer(line) with
        {
            Transform = new() { Pivot = new(20, 10) },
            Tracks = [rotation, new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(80, 50))])]
        };

        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] });

        Assert.Contains("\\org(", exported.Text, StringComparison.Ordinal);
        Assert.Contains("\\move(", exported.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Ass.Perspective", StringComparison.Ordinal) ||
            diagnostic.Code is "Subtitle.Composition" or "Ass.RotationOrigin");
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

    private static void AssertRotationEqual(SceneVector3 expected, SceneVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    private static AssImportResult Parse(string text, int? layoutHeight = 360, int playResHeight = 360) => AssSubtitleFormat.Parse($$"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: {{playResHeight}}
        {{(layoutHeight is { } height ? "LayoutResX: 640\nLayoutResY: " + height : string.Empty)}}
        WrapStyle: 1
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Spacing, Outline, Shadow, Alignment
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&HFF000000,100,100,0,0,0,7
        [Events]
        Format: Layer, Start, End, Style, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,{{text}}
        """, 640, 360);
}
