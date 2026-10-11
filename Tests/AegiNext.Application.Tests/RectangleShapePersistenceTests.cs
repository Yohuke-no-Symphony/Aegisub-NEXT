using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class RectangleShapePersistenceTests
{
    [Theory]
    [InlineData(ShapeKind.RECTANGLE)]
    [InlineData(ShapeKind.ROUNDED_RECTANGLE)]
    public void LegacyShapeWithoutFourCornersKeepsItsExistingAppearance(ShapeKind kind)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(kind, 100, 50) { CornerRadius = 12 }, Transform = new(10, 20, 2, 3, 30)
        };
        var source = new ProjectDocument { Layers = [layer] };
        var json = JsonNode.Parse(ProjectStore.Serialize(source))!;
        Assert.True(json["layers"]![0]!["shape"]!.AsObject().Remove("cornerRadii"));

        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        var actual = Assert.Single(loaded.Layers);
        Assert.Equal(layer.Shape, actual.Shape);
        Assert.Equal(layer.Transform, actual.Transform);
        Assert.Equal(ShapeGeometry.CreatePath(layer.Shape).Segments.ToArray(), ShapeGeometry.CreatePath(actual.Shape!).Segments.ToArray());
    }

    [Fact]
    public void IndependentCornersRoundTripAndAllFourFieldsAreRequiredWhenPresent()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 50) { CornerRadii = new(2, 4, 8, 16) };
        var source = new ProjectDocument { Layers = [new() { Kind = LayerKind.SHAPE, Shape = shape }] };
        var serialized = ProjectStore.Serialize(source);

        Assert.Equal(shape, Assert.Single(ProjectStore.Deserialize(serialized).Layers).Shape);

        var json = JsonNode.Parse(serialized)!;
        json["layers"]![0]!["shape"]!["cornerRadii"]!.AsObject().Remove("bottomLeft");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Fact]
    public void IndependentCornersConvertToTheSharedPathAndUndoAtomically()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 50) { CornerRadii = new(2, 4, 8, 16) };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = shape };
        var source = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(source);

        editor.Apply("Convert rectangle to path", document => ProjectEditingOperations.ConvertShapeToPath(document, layer.Id));

        var converted = Assert.Single(editor.Snapshot.Layers).Shape!;
        Assert.Equal(ShapeKind.PATH, converted.Kind);
        Assert.Equal(ShapeGeometry.CreatePath(shape).Segments.ToArray(), converted.Path!.Segments.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(source, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
