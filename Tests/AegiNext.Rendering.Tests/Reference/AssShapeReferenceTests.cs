using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssShapeReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(0)]
    [InlineData(90)]
    public void ExportedShapeKeepsNativeBoundsWithPivotRotationAndNonUniformScale(double rotation)
    {
        var document = new ProjectDocument
        {
            Width = 640, Height = 360,
            Layers = [new()
            {
                Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 60, 40), Fill = new(0, 1, 0),
                Transform = new(X: 200, Y: 100, ScaleX: 2, ScaleY: 0.5, Rotation: rotation, AnchorX: 10, AnchorY: 20)
            }]
        };
        using var native = ReferenceSubtitleProject.Renderer();
        var expected = ReferenceSubtitleProject.Render(native, document, 0);
        using var reference = new LibassReferenceRenderer();
        var actual = reference.Render(AssSubtitleFormat.Write(document).Text, 0);
        var first = expected.InkBounds();
        var second = actual.InkBounds();
        Assert.InRange(Math.Abs(first.Left - second.Left), 0, 1);
        Assert.InRange(Math.Abs(first.Top - second.Top), 0, 1);
        Assert.InRange(Math.Abs(first.Right - second.Right), 0, 1);
        Assert.InRange(Math.Abs(first.Bottom - second.Bottom), 0, 1);
        Assert.InRange(Math.Abs(actual.Energy(3) / expected.Energy(3) - 1), 0, 0.02);
    }
}
