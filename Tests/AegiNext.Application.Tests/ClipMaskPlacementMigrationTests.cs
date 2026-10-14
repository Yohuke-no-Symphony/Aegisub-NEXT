using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipMaskPlacementMigrationTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public async Task LegacyMasksPreserveGeometryAndEveryAnimationSampleAtAllLoadBoundaries(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        foreach (var vector in new[] { false, true })
        {
            var original = AnimatedDocument(vector);
            var pivot = new ScenePoint(230, -75);
            var root = LegacyRoot(original, version, pivot);
            var source = Encoding.UTF8.GetBytes(root.ToJsonString());
            var originalTracks = JsonNode.Parse(ProjectStore.Serialize(original))!["layers"]![0]!["tracks"]!.ToJsonString();
            var path = Path.Combine(directory.Path, "mask.aeginext");
            await File.WriteAllBytesAsync(path, source);
            using var parsed = JsonDocument.Parse(source);
            var loaded = new[]
            {
                ProjectStore.Deserialize(source), ProjectStore.Deserialize(parsed.RootElement), await ProjectStore.LoadAsync(path)
            };

            foreach (var document in loaded)
            {
                Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
                var layer = Assert.Single(document.Layers);
                var mask = Assert.IsAssignableFrom<ClipMask>(layer.Mask);
                Assert.Equal(original.Layers[0].Mask!.Transform, mask.Transform);
                Assert.Equal(ClipMaskPlacementGeometry.CaptureReferenceFrame(original.Layers[0].Mask!), mask.ReferenceFrame);
                Assert.Equal(new ScenePoint(pivot.X / document.Width, 0), mask.Placement.Anchor);
                AssertPoint(pivot, LayerPositionGeometry.BasePosition(mask.Placement, new(document.Width, document.Height)));
                AssertPoint(pivot, LayerPositionGeometry.LocalPivot(mask.Placement, mask.ReferenceFrame.Origin, mask.ReferenceFrame.Size));
                var current = JsonNode.Parse(ProjectStore.Serialize(document))!;
                Assert.Equal(originalTracks, current["layers"]![0]!["tracks"]!.ToJsonString());
                Assert.False(current["layers"]![0]!["mask"]!["transform"]!.AsObject().ContainsKey("pivot"));
                Assert.Equal(ProjectStore.Serialize(document), ProjectStore.Serialize(ProjectStore.Deserialize(ProjectStore.Serialize(document))));

                foreach (var time in new MediaTime[] { new(0), new(1, 3), new(1), new(7, 4), new(3) })
                {
                    var evaluated = SceneEvaluator.EvaluateMask(layer, time)!;
                    var matrix = ClipMaskPlacementGeometry.CreateMatrix(evaluated.Placement, evaluated.ReferenceFrame,
                        evaluated.Transform, new(document.Width, document.Height));
                    foreach (var point in Points(evaluated))
                    {
                        AssertPoint(LegacyMap(point, evaluated.Transform, pivot), matrix.Map(point));
                    }
                    Assert.Same(mask.Placement, evaluated.Placement);
                    Assert.Same(mask.ReferenceFrame, evaluated.ReferenceFrame);
                }
            }
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
    }

    [Fact]
    public void VersionSeventeenPreservesOrderedCurvesVectorRotationAndPresetTracks()
    {
        var document = AnimatedDocument(false);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(1, 3), new(5, 3), new ScenePoint(45, 65), 2)
        {
            ComponentMask = 1, Curve = new(KeyframeInterpolation.POWER, 0.2, 0.8) { Exponent = 1.7, Reverse = true }
        };
        var track = new AnimationTrack(AnimationProperty.MASK_POSITION, [])
        {
            InitialValue = new ScenePoint(13, -7),
            Transforms = [operation, new(Guid.NewGuid(), new(1), new(3), new ScenePoint(-20, 15), 0.5) { ComponentMask = 2 }]
        };
        var layer = document.Layers[0] with
        {
            Tracks = [track, new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(10, 20, 30))])]
        };
        document = document with
        {
            Layers = [layer], Presets = [new(Guid.NewGuid(), "mask", [track])]
        };
        var root = LegacyRoot(document, 17, new(40, 50));

        var migrated = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        var originalJson = JsonNode.Parse(ProjectStore.Serialize(document))!;
        var migratedJson = JsonNode.Parse(ProjectStore.Serialize(migrated))!;
        Assert.Equal(originalJson["layers"]![0]!["tracks"]!.ToJsonString(), migratedJson["layers"]![0]!["tracks"]!.ToJsonString());
        Assert.Equal(originalJson["presets"]!.ToJsonString(), migratedJson["presets"]!.ToJsonString());
        foreach (var time in new MediaTime[] { new(0), new(1, 3), new(1), new(5, 3), new(2), new(3) })
        {
            Assert.Equal(SceneEvaluator.EvaluateTrack(track, time), SceneEvaluator.EvaluateTrack(migrated.Layers[0].Tracks[0], time));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 30)]
    [InlineData(20, 0)]
    [InlineData(0.00000001, 0.00000001)]
    public void DegenerateAndSubpixelFramesPreserveExternalPixelPivots(double width, double height)
    {
        var document = AnimatedDocument(false);
        var mask = new RectangleClipMask
        {
            TopLeft = new(10, 20), BottomRight = new(10 + width, 20 + height),
            Transform = new() { Position = new(13, -7), Scale = new(-2, 3), Rotation = 31 }
        };
        document = document with { Layers = [document.Layers[0] with { Mask = mask, Tracks = [] }] };
        var pivot = new ScenePoint(-1e9, 1e9);
        var root = LegacyRoot(document, 17, pivot);

        var migrated = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var restored = Assert.IsType<RectangleClipMask>(migrated.Layers[0].Mask);

        Assert.Equal(new ScenePoint(Math.Max(width, 1), Math.Max(height, 1)), restored.ReferenceFrame.Size);
        Assert.Equal(new ScenePoint(0, 1), restored.Placement.Anchor);
        Assert.InRange(restored.Placement.Offset.X, -1e9, 1e9);
        Assert.InRange(restored.Placement.Offset.Y, -1e9, 1e9);
        var matrix = ClipMaskPlacementGeometry.CreateMatrix(restored.Placement, restored.ReferenceFrame,
            restored.Transform, new(migrated.Width, migrated.Height));
        AssertPoint(LegacyMap(mask.TopLeft, mask.Transform, pivot), matrix.Map(restored.TopLeft), 0.000001);
    }

    [Fact]
    public void CurveExtremaOutsideNodeBudgetStillAllowTheLegacyPivotToMigrate()
    {
        var document = AnimatedDocument(true);
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes =
            [
                new() { Position = new(-1e9, 0), OutHandle = new(-4d / 3, 0) },
                new() { Position = new(-1e9, 0), InHandle = new(-4d / 3, 0) }
            ] }]
        };
        document = document with { Layers = [document.Layers[0] with { Mask = mask, Tracks = [] }] };
        var root = LegacyRoot(document, 17, new(1e9, 0));

        var migrated = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var restored = Assert.IsType<VectorClipMask>(migrated.Layers[0].Mask);

        Assert.Equal(-1e9 - 1, restored.ReferenceFrame.Origin.X);
        Assert.Equal(2e9 + 1, restored.Placement.Pivot.X);
        Assert.Equal(mask.Contours.SelectMany(contour => contour.Nodes), restored.Contours.SelectMany(contour => contour.Nodes));
    }

    [Theory]
    [InlineData("placement")]
    [InlineData("referenceFrame")]
    public void LegacyVersionsRejectNativeFieldsEvenWhenNull(string field)
    {
        foreach (var version in new[] { 5, 17 })
        {
            var root = LegacyRoot(AnimatedDocument(false), version, new(40, 50));
            root["layers"]![0]!["mask"]![field] = null;
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
    }

    [Fact]
    public async Task InvalidLegacyPivotDoesNotBecomeValidAfterAnchorClampingAndLeavesTheFileUntouched()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = LegacyRoot(AnimatedDocument(false), 17, new(1e9 + 1, 50));
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var path = Path.Combine(directory.Path, "invalid.aeginext");
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedLegacyVectorGeometryFailsValidationBeforeCapturingItsReferenceFrame(bool node)
    {
        var root = LegacyRoot(AnimatedDocument(true), 17, new(40, 50));
        var contours = root["layers"]![0]!["mask"]!["contours"]!.AsArray();
        if (node)
        {
            contours[0]!["nodes"]!.AsArray()[0] = null;
        }
        else
        {
            contours[0] = null;
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("placement")]
    [InlineData("referenceFrame")]
    [InlineData("anchor")]
    [InlineData("pivot")]
    [InlineData("offset")]
    [InlineData("origin")]
    [InlineData("size")]
    [InlineData("legacyPivot")]
    [InlineData("unknown")]
    public void VersionEighteenRequiresCompletePlacementAndRejectsLegacyOrUnknownFields(string field)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(AnimatedDocument(false)))!.AsObject();
        var mask = root["layers"]![0]!["mask"]!.AsObject();
        if (field is "placement" or "referenceFrame")
        {
            mask.Remove(field);
        }
        else if (field is "anchor" or "pivot" or "offset")
        {
            mask["placement"]!.AsObject().Remove(field);
        }
        else if (field is "origin" or "size")
        {
            mask["referenceFrame"]!.AsObject().Remove(field);
        }
        else if (field == "legacyPivot")
        {
            mask["transform"]!["pivot"] = new JsonObject { ["x"] = 40, ["y"] = 50 };
        }
        else
        {
            mask["referenceFrame"]!["unknown"] = 1;
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static ProjectDocument AnimatedDocument(bool vector)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(3), "mask");
        var first = new MaskNode { Position = new(10, 20), OutHandle = new(5, -25) };
        ClipMask mask = vector ? new VectorClipMask
        {
            Contours = [new() { Nodes = [first, new() { Position = new(100, 80), InHandle = new(-15, 10) }] }]
        } : new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) };
        mask = mask with { Inverted = true, Transform = new() { Position = new(13, -7), Scale = new(2, -3), Rotation = 27 } };
        editor.SetClipMask(id, mask);
        editor.SetKeyframe(id, AnimationProperty.MASK_POSITION, new(new(0), new ScenePoint(13, -7), KeyframeInterpolation.POWER)
        {
            Exponent = 1.7, ComponentCurves = [new(KeyframeInterpolation.EASE_OUT, 0.1, 0.9)]
        });
        editor.SetKeyframe(id, AnimationProperty.MASK_POSITION, new(new(3), new ScenePoint(43, 23)));
        editor.SetKeyframe(id, AnimationProperty.MASK_SCALE, new(new(0), new ScenePoint(2, -3)));
        editor.SetKeyframe(id, AnimationProperty.MASK_SCALE, new(new(3), new ScenePoint(-1, 2)));
        editor.SetKeyframe(id, AnimationProperty.MASK_ROTATION, new(new(0), 27d));
        editor.SetKeyframe(id, AnimationProperty.MASK_ROTATION, new(new(3), 87d));
        var target = vector ? new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id) :
            new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        editor.SetKeyframe(id, target, new(new(0), new ScenePoint(10, 20)));
        editor.SetKeyframe(id, target, new(new(3), new ScenePoint(30, 45)));
        return editor.Snapshot;
    }

    private static JsonObject LegacyRoot(ProjectDocument document, int version, ScenePoint pivot)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = version;
        var mask = root["layers"]![0]!["mask"]!.AsObject();
        mask.Remove("placement");
        mask.Remove("referenceFrame");
        mask["transform"]!["pivot"] = new JsonObject { ["x"] = pivot.X, ["y"] = pivot.Y };
        LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        return root;
    }

    private static IEnumerable<ScenePoint> Points(ClipMask mask)
    {
        return mask is RectangleClipMask rectangle ? new[] { rectangle.TopLeft, rectangle.BottomRight } :
            ((VectorClipMask)mask).Contours.SelectMany(contour => contour.Nodes).Select(node => node.Position);
    }

    private static ScenePoint LegacyMap(ScenePoint point, MaskTransform transform, ScenePoint pivot)
    {
        var angle = transform.Rotation * Math.PI / 180;
        var x = (point.X - pivot.X) * transform.Scale.X;
        var y = (point.Y - pivot.Y) * transform.Scale.Y;
        return new(x * Math.Cos(angle) - y * Math.Sin(angle) + pivot.X + transform.Position.X,
            x * Math.Sin(angle) + y * Math.Cos(angle) + pivot.Y + transform.Position.Y);
    }

    private static void AssertPoint(ScenePoint expected, ScenePoint actual, double tolerance = 0.00000001)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }
}
