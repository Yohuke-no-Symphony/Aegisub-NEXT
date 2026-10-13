using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SpatialTransformPersistenceTests
{
    [Fact]
    public void VersionSixteenRoundTripsLayerAndRangeProjectionWithTracks()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2)
        {
            RotationX = -23, RotationY = 41, PerspectiveDistance = 800, ProjectionTranslation = new(7, 8, 9)
        };
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End,
            Transform = new() { RotationX = 33, RotationY = -60, PerspectiveDistance = 1200, ProjectionTranslation = new(-1, 2, 3) },
            Tracks = [new(AnimationProperty.ROTATION_Y, [new(new(0), -60), new(new(1), 720)])]
        };
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(new() { Subtitles = [line], Layers = [layer] }));

        Assert.Equal(16, loaded.Version);
        Assert.Equal(layer.Transform, loaded.Layers[0].Transform);
        Assert.Equal(layer.Tracks[0].Target, loaded.Layers[0].Tracks[0].Target);
        Assert.Equal(layer.Tracks[0].Keyframes.Select(frame => (frame.Time, frame.Value, Curve: frame.GetCurve(0))),
            loaded.Layers[0].Tracks[0].Keyframes.Select(frame => (frame.Time, frame.Value, Curve: frame.GetCurve(0))));
        Assert.Equal(range, loaded.Subtitles[0].AnimationRanges[0]);
    }

    [Fact]
    public void VersionFifteenDefaultsPreserveTheOriginalTwoDimensionalTransform()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 200, 100), Transform = new(30, 40, 2, -3, 43, 7, 11) };
        var json = JsonNode.Parse(ProjectStore.Serialize(new() { Layers = [layer] }))!;
        json["version"] = 15;
        foreach (var field in new[] { "rotationX", "rotationY", "perspectiveDistance", "projectionTranslation" })
        {
            json["layers"]![0]!["transform"]!.AsObject().Remove(field);
        }
        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(layer.Transform, loaded.Layers[0].Transform);
        Assert.Equal(312.5, loaded.Layers[0].Transform.PerspectiveDistance);
    }

    [Theory]
    [InlineData("rotationX")]
    [InlineData("rotationY")]
    [InlineData("perspectiveDistance")]
    public void OlderVersionsCannotDeclareSpatialFields(string field)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10) };
        var json = JsonNode.Parse(ProjectStore.Serialize(new() { Layers = [layer] }))!;
        json["version"] = 15;
        var transform = json["layers"]![0]!["transform"]!.AsObject();
        foreach (var name in new[] { "rotationX", "rotationY", "perspectiveDistance", "projectionTranslation" })
        {
            transform.Remove(name);
        }
        transform[field] = 1;

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
