using System.Text.Json.Nodes;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class LayerPositionPersistenceTests
{
    [Fact]
    public void AdvancedAssTextEditsPreserveTheNativeSubtitlePositionAfterSharingTheModel()
    {
        var position = new SubtitlePosition
        {
            Anchor = new(0.123456789, 0.87654321), Pivot = new(0.25, 0.75),
            Offset = new(12.3456789, -98.7654321)
        };
        var line = new SubtitleLine { Text = "Original", Style = new() { Position = position } };
        var projection = AssTextProjection.Create(line);

        var edited = AssTextProjection.Apply(line, projection.Source.Replace("Original", "Edited", StringComparison.Ordinal));

        Assert.Equal("Edited", edited.Line.Text);
        Assert.Same(position, edited.Line.Style.Position);
    }

    [Fact]
    public void LegacyGraphicWithoutNormalizedPositionKeepsItsPixelPivotAndTransform()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 200, 100),
            Transform = new(150, 250, 2, -3, 45, 23, 47)
        };
        var json = JsonNode.Parse(ProjectStore.Serialize(new() { Layers = [layer] }))!;
        Assert.True(json["layers"]![0]!.AsObject().Remove("position"));

        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(layer, Assert.Single(loaded.Layers));
        Assert.Null(loaded.Layers[0].Position);
    }

    [Fact]
    public void SharedPositionRoundTripsBothGraphicAndSubtitlePositionsWithoutTypeLoss()
    {
        var position = new LayerPosition { Anchor = new(0.5, 1), Pivot = new(1, 0), Offset = new(-25, 15) };
        var graphicTrack = new ProjectTrack { Name = "Graphics" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, TrackId = graphicTrack.Id, Shape = new(ShapeKind.RECTANGLE, 200, 100), Position = position,
            Transform = new(30, 40, 2, -3, 45, 5, 7)
        };
        var subtitle = new SubtitleLine { Text = "Text", Style = new() { Position = new() { Pivot = new(0.25, 0.75) } } };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, graphicTrack],
            Subtitles = [subtitle],
            Layers = [shape, new() { SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(shape, loaded.Layers[0]);
        Assert.IsType<LayerPosition>(loaded.Layers[0].Position);
        Assert.Equal(subtitle.Style.Position, loaded.Subtitles[0].Style.Position);
        Assert.IsType<SubtitlePosition>(loaded.Subtitles[0].Style.Position);
    }

    [Theory]
    [InlineData("anchor")]
    [InlineData("pivot")]
    [InlineData("offset")]
    public void DeclaredGraphicPositionRequiresAllOfItsComponents(string field)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10), Position = new()
        };
        var json = JsonNode.Parse(ProjectStore.Serialize(new() { Layers = [layer] }))!;
        Assert.True(json["layers"]![0]!["position"]!.AsObject().Remove(field));

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
