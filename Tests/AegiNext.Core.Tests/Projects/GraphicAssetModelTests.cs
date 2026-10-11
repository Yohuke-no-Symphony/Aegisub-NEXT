using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class GraphicAssetModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPlaybackSpeedIsRejected(double speed)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/animated.gif");
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Assets = [asset], Layers = [new() { Kind = LayerKind.IMAGE, Image = new(asset.Id, 20, 20) { Playback = new() { Speed = speed } } }]
        }));
    }

    [Fact]
    public void LinkedImagesAllowExternalReferencesButManagedImagesAndFontsDoNot()
    {
        var linked = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, string.Empty,
            ExternalPath: "/pictures/linked.png", Storage: ProjectAssetStorage.LINKED);
        ProjectValidator.Validate(new() { Assets = [linked] });
        Assert.Equal("/pictures/linked.png", ProjectAssetLocation.Resolve(linked, "/project"));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Assets = [linked with { Storage = ProjectAssetStorage.MANAGED }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Assets = [linked with { Kind = ProjectAssetKind.FONT }] }));
    }

    [Fact]
    public void FolderCyclesAndInvalidCropAreRejected()
    {
        var first = new ProjectAssetFolder(Guid.NewGuid(), "First");
        var second = new ProjectAssetFolder(Guid.NewGuid(), "Second", first.Id);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            AssetLibrary = new() { Folders = [first with { ParentId = second.Id }, second] }
        }));
        var image = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.png");
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Assets = [image], Layers = [new() { Kind = LayerKind.IMAGE, Image = new(image.Id, 100, 100) { Crop = new(0.9, 0, 0.2, 1) } }]
        }));
    }

    [Fact]
    public void CropPreservesSourcePhaseAndStretchKeepsPlaybackSpeedIndependentFromAnimation()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Start = new(10), End = new(20), AnimationOffset = new(7),
            Image = new(Guid.NewGuid(), 20, 20) { Playback = new() { SourceOffset = new(3), Speed = 1.5 } },
            Tracks = [new(AnimationProperty.OPACITY, [new(new(7), 0), new(new(17), 1)])]
        };
        var cropped = LayerAnimationTiming.Retime(layer, new(12), new(20), TimelineEditMode.CROP);
        Assert.Equal(new MediaTime(6), cropped.Image!.Playback!.SourceOffset);
        Assert.Equal(ImagePlaybackTiming.GetSourceTime(layer, new(15)), ImagePlaybackTiming.GetSourceTime(cropped, new(15)));
        var stretched = LayerAnimationTiming.Retime(layer, new(10), new(30), TimelineEditMode.STRETCH);
        Assert.Equal(layer.Image, stretched.Image);
        Assert.Equal(new MediaTime(14), stretched.AnimationOffset);
        Assert.Equal(new MediaTime(9), ImagePlaybackTiming.GetSourceTime(stretched, new(14)));
    }

    [Fact]
    public void ExtendingCropLeftSupportsNegativeSourceTimeAndFractionalFrameTimes()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Start = new(10), End = new(12),
            Image = new(Guid.NewGuid(), 20, 20) { Playback = new() { Speed = 0.1 } }
        };
        var extended = LayerAnimationTiming.Retime(layer, new(8), new(12), TimelineEditMode.CROP);
        Assert.Equal(new MediaTime(-1, 5), extended.Image!.Playback!.SourceOffset);
        var time = new MediaTime(300001, 30000);
        Assert.Equal(new MediaTime(1, 300000), ImagePlaybackTiming.GetSourceTime(layer, time));
        Assert.Equal(ImagePlaybackTiming.GetSourceTime(layer, time), ImagePlaybackTiming.GetSourceTime(extended, time));
    }

    [Fact]
    public void ResizingFreeGeometryScalesEveryPointWithoutChangingTheOriginalOrClosingIt()
    {
        var path = new PathGeometry(new(1, 2), [new(new(3, 4), new(5, 6), new(7, 8))]);
        var shape = new LayerShape(ShapeKind.PATH, 10, 20, path);

        var resized = ShapeGeometry.Resize(shape, 20, 10);

        Assert.Equal(20, resized.Width);
        Assert.Equal(10, resized.Height);
        Assert.Equal(new ScenePoint(2, 1), resized.Path!.Start);
        Assert.Equal(new CubicBezierSegment(new(6, 2), new(10, 3), new(14, 4)), Assert.Single(resized.Path.Segments));
        Assert.False(resized.Path.Closed);
        Assert.Same(path, shape.Path);
        Assert.Equal(new ScenePoint(1, 2), shape.Path!.Start);
        Assert.Same(shape, ShapeGeometry.Resize(shape, 10, 20));
    }

    [Fact]
    public void ResizingParametricGeometryPreservesItsParametersAndRejectsInvalidDimensions()
    {
        var shape = new LayerShape(ShapeKind.ROUNDED_RECTANGLE, 100, 40) { CornerRadius = 8 };

        var resized = ShapeGeometry.Resize(shape, 50, 20);

        Assert.Equal(shape with { Width = 50, Height = 20 }, resized);
        Assert.Throws<InvalidDataException>(() => ShapeGeometry.Resize(shape, 0, 20));
        Assert.Throws<InvalidDataException>(() => ShapeGeometry.Resize(shape, double.PositiveInfinity, 20));
    }

    [Fact]
    public void PrimitiveGeometryHasStableBoundsAndOpenLines()
    {
        var line = ShapeGeometry.CreatePath(new(ShapeKind.LINE, 100, 40));
        Assert.False(line.Closed);
        Assert.Equal(new ScenePoint(0, 20), line.Start);
        Assert.Equal(new ScenePoint(100, 20), Assert.Single(line.Segments).End);
        var arrow = ShapeGeometry.CreatePath(new(ShapeKind.ARROW, 100, 40));
        Assert.True(arrow.Closed);
        Assert.Contains(arrow.Segments, segment => segment.End == new ScenePoint(100, 20));
        Assert.Equal(8, ShapeGeometry.CreatePath(new(ShapeKind.ROUNDED_RECTANGLE, 100, 40) { CornerRadius = 8 }).Segments.Length);
        Assert.Equal(4, ShapeGeometry.CreatePath(new(ShapeKind.ELLIPSE, 100, 40)).Segments.Length);
    }
}
