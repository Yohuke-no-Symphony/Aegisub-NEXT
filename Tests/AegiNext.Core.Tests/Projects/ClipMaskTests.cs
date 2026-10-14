using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ClipMaskTests
{
    [Fact]
    public void RectangleAndZeroAreaMaskAreValidOnSubtitleClips()
    {
        var rectangle = new RectangleClipMask
        {
            TopLeft = new(10, 20),
            BottomRight = new(300, 400),
            Inverted = true,
            Placement = new() { Pivot = new(0.5, 0.5), Offset = new(155, 210) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(290, 380) },
            Transform = new()
            {
                Position = new(5, -8),
                Scale = new(2, 0.5),
                Rotation = 90
            }
        };

        ProjectValidator.Validate(CreateProject(rectangle));
        ProjectValidator.Validate(CreateProject(rectangle with { BottomRight = rectangle.TopLeft }));
    }

    [Fact]
    public void MultiContourMaskPreservesStableIdentityAndRelativeHandlesAfterSerialization()
    {
        var first = new MaskNode
        {
            Position = new(20, 30),
            InHandle = new(-5, -8),
            OutHandle = new(6, 9)
        };
        var second = new MaskNode { Position = new(180, 220) };
        ClipMask mask = new VectorClipMask
        {
            Contours =
            [
                new() { Nodes = [first, second] },
                new() { Nodes = [new() { Position = new(60, 80) }] }
            ],
            Placement = new() { Pivot = new(0.5, 0.5), Offset = new(100, 125) },
            ReferenceFrame = new() { Origin = new(20, 30), Size = new(160, 190) }
        };

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var encoded = JsonSerializer.Serialize(mask, options);
        var restored = Assert.IsType<VectorClipMask>(JsonSerializer.Deserialize<ClipMask>(encoded, options));
        var original = Assert.IsType<VectorClipMask>(mask);
        Assert.Contains("\"kind\":\"VECTOR\"", encoded, StringComparison.Ordinal);
        Assert.Equal(original.Contours.Select(contour => contour.Id), restored.Contours.Select(contour => contour.Id));
        Assert.Equal(original.Contours.SelectMany(contour => contour.Nodes).Select(node => node.Id),
            restored.Contours.SelectMany(contour => contour.Nodes).Select(node => node.Id));
        Assert.Equal(first.Position, restored.Contours[0].Nodes[0].Position);
        Assert.Equal(first.InHandle, restored.Contours[0].Nodes[0].InHandle);
        Assert.Equal(first.OutHandle, restored.Contours[0].Nodes[0].OutHandle);
        Assert.Equal(mask.Transform, restored.Transform);
        Assert.Equal(mask.Placement, restored.Placement);
        Assert.Equal(mask.ReferenceFrame, restored.ReferenceFrame);
        ProjectValidator.Validate(CreateProject(restored));
    }

    [Fact]
    public void RectangleMaskUsesExplicitPolymorphicKindAndIdentityTransformDefaults()
    {
        ClipMask mask = new RectangleClipMask { TopLeft = new(-30, 40), BottomRight = new(80, 90) };
        var encoded = JsonSerializer.Serialize(mask);
        var restored = Assert.IsType<RectangleClipMask>(JsonSerializer.Deserialize<ClipMask>(encoded));

        Assert.Contains("\"kind\":\"RECTANGLE\"", encoded, StringComparison.Ordinal);
        Assert.Equal(mask, restored);
        Assert.Equal(new ScenePoint(1, 1), restored.Transform.Scale);
        Assert.Equal(default, restored.Transform.Position);
        Assert.Equal(new LayerPosition(), restored.Placement);
        Assert.Equal(new MaskReferenceFrame(), restored.ReferenceFrame);
        Assert.Equal(0, restored.Transform.Rotation);
    }

    [Theory]
    [InlineData(LayerKind.SHAPE)]
    [InlineData(LayerKind.IMAGE)]
    public void NonSubtitleLayersCannotHaveMasks(LayerKind kind)
    {
        var layer = new ProjectLayer
        {
            Kind = kind,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 100, 100) : null,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) }
        };

        var failure = Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer] }));
        Assert.Contains("subtitle", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaskRequiresAnExistingSubtitleReference()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE,
            SubtitleId = Guid.NewGuid(),
            Mask = new RectangleClipMask { BottomRight = new(100, 100) }
        };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [layer with { SubtitleId = null }]
        }));
    }

    [Fact]
    public void RectangleRequiresOrderedFiniteBoundsAndValidIndependentTransform()
    {
        var rectangle = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(30, 40) };

        AssertInvalid(rectangle with { TopLeft = new(31, 20) });
        AssertInvalid(rectangle with { BottomRight = new(30, 19) });
        AssertInvalid(rectangle with { TopLeft = new(double.NaN, 20) });
        AssertInvalid(rectangle with { BottomRight = new(30, double.PositiveInfinity) });
        AssertInvalid(rectangle with { Transform = null! });
        AssertInvalid(rectangle with { Transform = new() { Position = new(double.NaN, 0) } });
        AssertInvalid(rectangle with { Transform = new() { Scale = new(1, double.NegativeInfinity) } });
        AssertInvalid(rectangle with { Transform = new() { Rotation = double.NaN } });
        AssertInvalid(rectangle with { Placement = null! });
        AssertInvalid(rectangle with { ReferenceFrame = null! });
        AssertInvalid(rectangle with { Placement = new() { Pivot = new(double.PositiveInfinity, 0) } });
        AssertInvalid(rectangle with { Placement = new() { Anchor = new(1.01, 0) } });
        AssertInvalid(rectangle with { Placement = new() { Offset = new(1e9 + 1, 0) } });
        AssertInvalid(rectangle with { ReferenceFrame = new() { Origin = new(2e9 + 1, 0) } });
        AssertInvalid(rectangle with { ReferenceFrame = new() { Size = new(0.5, 1) } });
        AssertInvalid(rectangle with { ReferenceFrame = new() { Size = new(1, double.NaN) } });
    }

    [Fact]
    public void VectorMaskRejectsEmptyOrDuplicateContourAndNodeIdentity()
    {
        var node = new MaskNode { Position = new(10, 20) };
        var contour = new MaskContour { Nodes = [node] };
        var vector = new VectorClipMask { Contours = [contour] };

        ProjectValidator.Validate(CreateProject(vector));
        AssertInvalid(vector with { Contours = default });
        AssertInvalid(vector with { Contours = [] });
        AssertInvalid(vector with { Contours = [null!] });
        AssertInvalid(vector with { Contours = [contour with { Id = Guid.Empty }] });
        AssertInvalid(vector with { Contours = [contour, contour] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = default }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [] }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [null!] }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [node with { Id = Guid.Empty }] }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [node, node] }] });
        AssertInvalid(vector with { Contours = [contour, new() { Nodes = [node] }] });
        AssertInvalid(vector with { Contours = [contour with { Id = node.Id }] });
    }

    [Fact]
    public void VectorMaskValidatesNodePositionAndBothRelativeHandles()
    {
        var node = new MaskNode { Position = new(10, 20), InHandle = new(-4, 5), OutHandle = new(6, -7) };
        var contour = new MaskContour { Nodes = [node] };
        var vector = new VectorClipMask { Contours = [contour] };

        ProjectValidator.Validate(CreateProject(vector));
        AssertInvalid(vector with { Contours = [contour with { Nodes = [node with { Position = new(double.NaN, 20) }] }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [node with { InHandle = new(0, double.PositiveInfinity) }] }] });
        AssertInvalid(vector with { Contours = [contour with { Nodes = [node with { OutHandle = new(double.NegativeInfinity, 0) }] }] });
    }

    [Fact]
    public void VectorNodeBudgetAppliesAcrossAllContours()
    {
        var nodes = Enumerable.Range(0, 10000)
            .Select(index => new MaskNode { Position = new(index, index) })
            .ToImmutableArray();
        var vector = new VectorClipMask
        {
            Contours = [new() { Nodes = nodes[..5000] }, new() { Nodes = nodes[5000..] }]
        };

        ProjectValidator.Validate(CreateProject(vector));
        AssertInvalid(vector with { Contours = vector.Contours.Add(new() { Nodes = [new()] }) });
    }

    private static ProjectDocument CreateProject(ClipMask mask)
    {
        var subtitle = new SubtitleLine { Text = "字幕" };
        return new()
        {
            Subtitles = [subtitle],
            Layers =
            [
                new()
                {
                    Kind = LayerKind.SUBTITLE,
                    SubtitleId = subtitle.Id,
                    Start = subtitle.Start,
                    End = subtitle.End,
                    Mask = mask
                }
            ]
        };
    }

    private static void AssertInvalid(ClipMask mask)
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(CreateProject(mask)));
    }
}
