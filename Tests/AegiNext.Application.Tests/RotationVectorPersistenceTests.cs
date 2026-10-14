using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class RotationVectorPersistenceTests
{
    [Fact]
    public async Task VersionSixteenAxesBecomeOneExactVectorAtEveryLoadBoundary()
    {
        using var directory = new TemporaryProjectDirectory();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { RotationX = 19, RotationY = 23, Rotation = 29 };
        var line = new SubtitleLine { Text = "AB", End = new(3), AnimationRanges = [range] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End,
            Transform = new() { RotationX = 7, RotationY = 11, Rotation = 13 },
            Tracks =
            [
                new(AnimationProperty.ROTATION_X,
                    [new(new(0), 10, KeyframeInterpolation.EASE_IN), new(new(2), 70)]),
                new(AnimationProperty.ROTATION,
                    [new(new(0), -720), new(new(1), 0), new(new(2), 720)]),
                new(new AnimationTrackTarget(AnimationProperty.ROTATION_Y, TextRangeId: range.Id),
                    [new(new(0), 23), new(new(2), -67)])
            ]
        };
        var original = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var root = LegacyRoot(original, 16);
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var path = Path.Combine(directory.Path, "legacy.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        using var parsed = JsonDocument.Parse(bytes);

        var loaded = new[]
        {
            ProjectStore.Deserialize(bytes), ProjectStore.Deserialize(parsed.RootElement), await ProjectStore.LoadAsync(path)
        };
        foreach (var document in loaded)
        {
            Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
            Assert.Equal(2, document.Layers[0].Tracks.Length);
            Assert.All(document.Layers[0].Tracks, track => Assert.Equal(AnimationProperty.ROTATION_3D, track.Property));
            var whole = document.Layers[0].Tracks.Single(track => track.Target.TextRangeId is null);
            Assert.Equal(new MediaTime[] { new(0), new(1), new(2) }, whole.Keyframes.Select(frame => frame.Time));
            Assert.Equal(3, whole.Keyframes[0].Value.ComponentCount);
            foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1), new(7, 4), new(2) })
            {
                var expected = Assert.Single(SceneEvaluator.Evaluate(original, time));
                var actual = Assert.Single(SceneEvaluator.Evaluate(document, time));
                Assert.Equal(expected.Transform.RotationX, actual.Transform.RotationX, 10);
                Assert.Equal(expected.Transform.RotationY, actual.Transform.RotationY, 10);
                Assert.Equal(expected.Transform.Rotation, actual.Transform.Rotation, 10);
                Assert.Equal(expected.Subtitle!.AnimationRanges[0].RotationY, actual.Subtitle!.AnimationRanges[0].RotationY, 10);
            }
            Assert.Equal(ProjectStore.Serialize(document), ProjectStore.Serialize(ProjectStore.Deserialize(ProjectStore.Serialize(document))));
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void SavingNormalizesAxesPresetsAndTimelineWithoutMutatingTheSnapshot()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10),
            Tracks = [new(AnimationProperty.ROTATION, [new(new(0), 13), new(new(1), 733)])]
        };
        var document = new ProjectDocument
        {
            Layers = [layer],
            Presets = [new(Guid.NewGuid(), "turn", layer.Tracks)],
            TimelineViewState = new()
            {
                ExpandedAnimationClips =
                [
                    new(layer.Id, new(AnimationProperty.ROTATION)),
                    new(layer.Id, new(AnimationProperty.ROTATION_X))
                ]
            }
        };
        var bytes = ProjectStore.Serialize(document);
        var loaded = ProjectStore.Deserialize(bytes);

        Assert.Equal(AnimationProperty.ROTATION, Assert.Single(document.Layers[0].Tracks).Property);
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(loaded.Layers[0].Tracks).Property);
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(loaded.Presets[0].Tracks).Property);
        Assert.Equal(AnimationProperty.ROTATION_3D, Assert.Single(loaded.TimelineViewState.ExpandedAnimationClips).Target.Property);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), ProjectStore.ComputeFingerprint(document));
        ProjectStore.ValidateSerialization(document);
    }

    [Fact]
    public void MixedLegacyAxesRoundTripOrderedComponentCurvesAndRationalTimesExactly()
    {
        var axes = new AnimationTrack[]
        {
            new(AnimationProperty.ROTATION_X, [])
            {
                InitialValue = 10d,
                Transforms = [new(Guid.NewGuid(), new(1, 3), new(7, 3), 70d, 2)]
            },
            new(AnimationProperty.ROTATION_Y,
            [
                new(new(1, 4), -20d, KeyframeInterpolation.EASE_IN)
                {
                    CurveStart = 0.2, CurveEnd = 0.8, Reverse = true
                },
                new(new(9, 4), 60d)
            ])
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10),
            Transform = new() { Rotation = 17 }, Tracks = [.. axes]
        };
        var root = LegacyRoot(new() { Layers = [layer] }, 16);
        var migrated = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var track = Assert.Single(migrated.Layers[0].Tracks);
        Assert.True(track.IsOrdered);
        var curved = Assert.Single(track.Transforms, operation => operation.Curve is not null);
        Assert.Equal(new MediaTime(1, 4), curved.Start);
        Assert.Equal(new MediaTime(9, 4), curved.End);
        Assert.True(curved.Curve!.Reverse);
        Assert.Equal(0.2, curved.Curve.CurveStart);
        Assert.Equal(0.8, curved.Curve.CurveEnd);

        var reopened = ProjectStore.Deserialize(ProjectStore.Serialize(migrated));
        var restored = Assert.Single(reopened.Layers[0].Tracks);
        Assert.Equal(track.Transforms.ToArray(), restored.Transforms.ToArray());
        foreach (var time in new MediaTime[] { new(0), new(1, 4), new(1, 3), new(13, 12), new(9, 4), new(7, 3), new(3) })
        {
            var vector = SceneEvaluator.EvaluateTrack(restored, time).Vector3;
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(axes[0], time), vector.X, 11);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(axes[1], time), vector.Y, 11);
            Assert.Equal(17, vector.Z);
        }
    }

    [Fact]
    public void VersionSixteenCannotDeclareOrderedComponentCurves()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10),
            Tracks =
            [
                new(AnimationProperty.ROTATION, [])
                {
                    InitialValue = 0d, Transforms = [new(Guid.NewGuid(), new(0), new(1), 90d)]
                }
            ]
        };
        var root = LegacyRoot(new() { Layers = [layer] }, 16);
        root["layers"]![0]!["tracks"]![0]!["transforms"]![0]!["curve"] = null;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void OlderVersionsCannotDeclareVectorRotation(int version)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10),
            Tracks = [new(AnimationProperty.ROTATION_3D, [new(new(0), new SceneVector3(7, 11, 13))])]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(new() { Layers = [layer] }))!;
        root["version"] = version;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void TimelineDuplicateIdentitiesAreRejectedBeforeNormalization()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10) };
        var clip = new TimelineEffectClipId(layer.Id, new(AnimationProperty.ROTATION));
        var document = new ProjectDocument
        {
            Layers = [layer], TimelineViewState = new() { ExpandedAnimationClips = [clip, clip] }
        };
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));
    }

    private static JsonObject LegacyRoot(ProjectDocument document, int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = version;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new MediaTimeJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        for (var index = 0; index < document.Layers.Length; index++)
        {
            root["layers"]![index]!["tracks"] = JsonSerializer.SerializeToNode(document.Layers[index].Tracks, options);
        }
        return root;
    }
}
