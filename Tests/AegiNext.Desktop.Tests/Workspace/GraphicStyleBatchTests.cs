using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class GraphicStyleBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageDimensionsApplyOnlyEditedAndLinkedAxesWithOneUndo(bool locked)
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Image;
        vm.AspectRatioLocked = locked;
        vm.Width.RawText = "300";

        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with
            {
                Image = original.Image! with
                {
                    Width = 300,
                    Height = locked ? original.Image!.Height * 300 / original.Image.Width : original.Image!.Height
                }
            }, changed.Layers[index]);
        }
        Assert.Same(source.Layers[2], changed.Layers[2]);
        Assert.Equal(source.Assets, changed.Assets);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedImageDimensionsUseTheLastExplicitlyEditedAxisForEachClipsOwnRatio(bool widthLast)
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Image;
        vm.Width.RawText = "300";
        vm.Height.RawText = "200";
        if (widthLast)
        {
            vm.Width.RawText = "600";
        }

        Assert.True(session.TryCommitDrafts(false));

        var primary = context.Editor.Snapshot.Layers[0].Image!;
        var secondary = context.Editor.Snapshot.Layers[1].Image!;
        Assert.Equal(widthLast ? 600 : 400, primary.Width);
        Assert.Equal(widthLast ? 300 : 200, primary.Height);
        Assert.Equal(widthLast ? 600 : 200d / 3, secondary.Width, 10);
        Assert.Equal(widthLast ? 1800 : 200, secondary.Height);
        Assert.Same(source.Layers[2], context.Editor.Snapshot.Layers[2]);
    }

    [Fact]
    public async Task RestoringThePairedImageDimensionPreservesThatDimensionOnEachOtherClip()
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Image;
        vm.Width.RawText = "300";

        Assert.True(vm.RestoreField("LayerHeightInput"));
        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with { Image = original.Image! with { Width = 300 } }, context.Editor.Snapshot.Layers[index]);
        }
    }

    [Fact]
    public async Task ImageCropAndPlaybackComponentsPreserveEachClipOtherSettingsAndDisabledPlayback()
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [.. source.Layers.Select(layer => layer.Id)]);
        var vm = session.ViewModel.Styles.Image;
        vm.CropWidth.RawText = "40";
        vm.PlaybackSpeed.RawText = "2";

        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < source.Layers.Length; index++)
        {
            var original = source.Layers[index];
            var image = original.Image!;
            Assert.Equal(original with
            {
                Image = image with
                {
                    Crop = image.Crop with { Width = 0.4 },
                    Playback = image.Playback is { } playback ? playback with { Speed = 2 } : null
                }
            }, context.Editor.Snapshot.Layers[index]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitImagePlaybackToggleKeepsUntouchedPlaybackParameters(bool enabled)
    {
        var source = ImageDocument();
        if (enabled)
        {
            source = source with
            {
                Layers = source.Layers.SetItem(0, source.Layers[0] with
                {
                    Image = source.Layers[0].Image! with { Playback = null }
                })
            };
        }
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [.. source.Layers.Select(layer => layer.Id)]);
        session.ViewModel.Styles.Image.AnimationEnabled = enabled;

        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < source.Layers.Length; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with
            {
                Image = original.Image! with { Playback = enabled ? original.Image!.Playback ?? new ImagePlayback() : null }
            }, context.Editor.Snapshot.Layers[index]);
        }
    }

    [Fact]
    public async Task InvalidMergedCropRejectsTheWholeBatchAndRestoringItsFieldRemovesItsIntent()
    {
        var source = ImageDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(0, source.Layers[0] with
                { Image = source.Layers[0].Image! with { Crop = new(0, 0, 0.5, 1) } })
                .SetItem(1, source.Layers[1] with
                    { Image = source.Layers[1].Image! with { Crop = new(0.05, 0, 0.9, 1) } })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Image;
        vm.CropX.RawText = "20";

        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal("ImageCropWidthInput", session.ViewModel.InvalidFieldKey);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(vm.RestoreField("ImageCropXInput"));
        vm.Fit = (int)ImageFit.STRETCH;
        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with { Image = original.Image! with { Fit = ImageFit.STRETCH } },
                context.Editor.Snapshot.Layers[index]);
        }
        Assert.Same(source.Layers[2], context.Editor.Snapshot.Layers[2]);
    }

    [Fact]
    public async Task CancelingLockedImageDimensionGestureDoesNotApplyRestoredPrimaryDimensionsToOtherClips()
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Image;
        vm.CropWidth.RawText = "40";
        var restore = Assert.IsType<Action>(vm.CaptureNumericGesture("LayerWidthInput"));
        vm.Width.RawText = "300";

        restore();
        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with
            {
                Image = original.Image! with { Crop = original.Image!.Crop with { Width = 0.4 } }
            }, context.Editor.Snapshot.Layers[index]);
        }
    }

    [Fact]
    public async Task ResettingAnAlreadyFullPrimaryCropStillResetsOtherSelectedCrops()
    {
        var source = ImageDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(0, source.Layers[0] with
                { Image = source.Layers[0].Image! with { Crop = new() } })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);

        session.ViewModel.Styles.Image.ResetCropCommand.Execute(null);

        Assert.Equal(source.Layers[1].Image! with { Crop = new() }, context.Editor.Snapshot.Layers[1].Image);
        Assert.Same(source.Layers[2], context.Editor.Snapshot.Layers[2]);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ShapeDimensionsResizeEachClipsOwnPathAndColorComponentsPreserveOtherComponents()
    {
        var source = ShapeDocument();
        var pathShape = new LayerShape(ShapeKind.ARROW, 80, 90) { ArrowHeadRatio = 0.6, ArrowShaftRatio = 0.8 };
        source = source with
        {
            Layers = source.Layers.SetItem(1, source.Layers[1] with
            {
                Shape = new(ShapeKind.PATH, pathShape.Width, pathShape.Height, ShapeGeometry.CreatePath(pathShape))
            })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Shape;
        vm.Width.RawText = "160";
        vm.FillDraft.Alpha.RawText = "0.5";

        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        Assert.Equal(source.Layers[0] with
        {
            Shape = source.Layers[0].Shape! with { Width = 160 },
            Fill = source.Layers[0].Fill with { Alpha = 0.5 }
        }, changed.Layers[0]);
        var expected = ShapeGeometry.Resize(source.Layers[1].Shape!, 160, 90);
        Assert.Equal(ShapeKind.PATH, changed.Layers[1].Shape!.Kind);
        Assert.Equal(160, changed.Layers[1].Shape!.Width);
        Assert.Equal(90, changed.Layers[1].Shape!.Height);
        Assert.Equal(expected.Path!.Start, changed.Layers[1].Shape!.Path!.Start);
        Assert.Equal(expected.Path.Segments.ToArray(), changed.Layers[1].Shape!.Path!.Segments.ToArray());
        Assert.Equal(source.Layers[1].Fill with { Alpha = 0.5 }, changed.Layers[1].Fill);
        Assert.Equal(source.Layers[1].Stroke, changed.Layers[1].Stroke);
        Assert.Same(source.Layers[2], changed.Layers[2]);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShapeCornerEditsApplyOnlyTheSelectedOrLinkedCorners(bool linked)
    {
        var source = ShapeDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(1, source.Layers[1] with
            {
                Shape = source.Layers[1].Shape! with { Kind = ShapeKind.ROUNDED_RECTANGLE, CornerRadii = null, CornerRadius = 20 }
            })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Shape;
        vm.CornersLinked = linked;
        vm.CornerTopLeft.RawText = "12";

        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            var corners = original.Shape!.EffectiveCornerRadii;
            var expected = linked ? ShapeCornerRadii.Uniform(12) : corners with { TopLeft = 12 };
            Assert.Equal(original with { Shape = original.Shape! with { CornerRadii = expected } },
                context.Editor.Snapshot.Layers[index]);
        }
        Assert.Same(source.Layers[2], context.Editor.Snapshot.Layers[2]);
    }

    [Fact]
    public async Task CancelingLinkedCornerGesturePreservesOtherDraftsAndOtherClipsOriginalCorners()
    {
        var source = ShapeDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Shape;
        vm.CornersLinked = true;
        vm.Width.RawText = "150";
        var restore = Assert.IsType<Action>(vm.CaptureNumericGesture("ShapeCornerTopLeftInput"));
        vm.CornerTopLeft.RawText = "12";

        restore();
        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with { Shape = original.Shape! with { Width = 150 } }, context.Editor.Snapshot.Layers[index]);
        }
    }

    [Fact]
    public async Task ShapeTypeAndArrowEditsKeepEachClipsUntouchedRatiosAndOwnFreePath()
    {
        var source = ShapeDocument();
        var path = new LayerShape(ShapeKind.ELLIPSE, 40, 30);
        source = source with
        {
            Layers = source.Layers.SetItem(1, source.Layers[1] with
            {
                Shape = source.Layers[1].Shape! with { Kind = ShapeKind.ELLIPSE, ArrowHeadRatio = 0.7, ArrowShaftRatio = 0.8 }
            }).SetItem(2, source.Layers[2] with
                { Shape = new(ShapeKind.PATH, path.Width, path.Height, ShapeGeometry.CreatePath(path)) })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [.. source.Layers.Select(layer => layer.Id)]);
        var vm = session.ViewModel.Styles.Shape;
        vm.ShapeType = 3;
        vm.ArrowHead.RawText = "25";

        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with
            {
                Shape = original.Shape! with { Kind = ShapeKind.ARROW, ArrowHeadRatio = 0.25 }
            }, context.Editor.Snapshot.Layers[index]);
        }
        Assert.Same(source.Layers[2], context.Editor.Snapshot.Layers[2]);
    }

    [Fact]
    public async Task AnimatedShapeAppearanceEditsUseEachClipsOwnFrozenTimeAndPreserveUntouchedColorComponents()
    {
        var source = ShapeDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(0, source.Layers[0] with
            {
                Tracks =
                [
                    new(AnimationProperty.STROKE_WIDTH, [new(new(0), 1), new(new(10), 3)]),
                    new(AnimationProperty.FILL, [new(new(0), new SceneColor(0.2, 0.4, 0.6, 0.8)), new(new(10), SceneColor.White)])
                ]
            }).SetItem(1, source.Layers[1] with
            {
                AnimationOffset = new(3),
                Tracks =
                [
                    new(AnimationProperty.STROKE_WIDTH, [new(new(3), 2), new(new(13), 4)]),
                    new(AnimationProperty.FILL, [new(new(3), new SceneColor(0.3, 0.5, 0.7, 0.9)), new(new(13), SceneColor.White)])
                ]
            })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [.. source.Layers.Select(layer => layer.Id)]);
        var vm = session.ViewModel.Styles.Shape;
        vm.StrokeWidth.RawText = "8";
        vm.FillDraft.Red.RawText = "0.9";

        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            var changed = context.Editor.Snapshot.Layers[index];
            Assert.Equal(original.StrokeWidth, changed.StrokeWidth);
            Assert.Equal(original.Fill, changed.Fill);
            var width = changed.Tracks.Single(track => track.Property == AnimationProperty.STROKE_WIDTH);
            Assert.Equal(new MediaTime(index == 0 ? 0 : 3), width.Keyframes[0].Time);
            Assert.Equal(8, width.Keyframes[0].Value.Scalar);
            Assert.Equal(original.Tracks[0].Keyframes[1], width.Keyframes[1]);
            var color = changed.Tracks.Single(track => track.Property == AnimationProperty.FILL);
            Assert.Equal(original.Tracks[1].Keyframes[0].Value.Color with { Red = 0.9 }, color.Keyframes[0].Value.Color);
            Assert.Equal(original.Tracks[1].Keyframes[1], color.Keyframes[1]);
        }
        Assert.Equal(source.Layers[2] with
        {
            StrokeWidth = 8, Fill = source.Layers[2].Fill with { Red = 0.9 }
        }, context.Editor.Snapshot.Layers[2]);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task OrderedAppearanceOnAnyTargetRejectsTheWholeBatchUntilThatFieldIsRestored()
    {
        var source = ShapeDocument();
        source = source with
        {
            Layers = source.Layers.SetItem(1, source.Layers[1] with
            {
                Tracks = [new(AnimationProperty.STROKE_WIDTH, [])
                {
                    InitialValue = 2, Transforms = [new(Guid.NewGuid(), new(0), new(10), 4)]
                }]
            })
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id, source.Layers[1].Id]);
        var vm = session.ViewModel.Styles.Shape;
        Assert.False(vm.CanEditStrokeWidth);
        vm.Width.RawText = "150";
        vm.StrokeWidth.RawText = "8";

        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal("StrokeWidthInput", session.ViewModel.InvalidFieldKey);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(vm.RestoreField("StrokeWidthInput"));
        Assert.True(session.TryCommitDrafts(false));

        for (var index = 0; index < 2; index++)
        {
            var original = source.Layers[index];
            Assert.Equal(original with { Shape = original.Shape! with { Width = 150 } }, context.Editor.Snapshot.Layers[index]);
        }
    }

    private static ProjectDocument ImageDocument()
    {
        var first = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/first.png");
        var second = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/second.png");
        var third = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/third.png");
        var tracks = Enumerable.Range(0, 3).Select(index => new ProjectTrack { Name = $"Image {index + 1}" }).ToArray();
        return new()
        {
            Tracks = [ProjectTrack.Default, .. tracks],
            Assets = [first, second, third],
            Layers =
            [
                new()
                {
                    Kind = LayerKind.IMAGE, TrackId = tracks[0].Id, Image = new(first.Id, 200, 100)
                    {
                        Crop = new(0.05, 0.1, 0.8, 0.8), Playback = new() { SourceOffset = new(1), Speed = 1 }
                    }
                },
                new()
                {
                    Kind = LayerKind.IMAGE, TrackId = tracks[1].Id, Image = new(second.Id, 80, 240, ImageFit.FILL_CROP)
                    {
                        Crop = new(0.1, 0.2, 0.6, 0.5),
                        Playback = new() { SourceOffset = new(5), Speed = 3, Mode = ImagePlaybackMode.FILE }
                    }
                },
                new()
                {
                    Kind = LayerKind.IMAGE, TrackId = tracks[2].Id, Image = new(third.Id, 50, 60)
                    {
                        Crop = new(0.2, 0.1, 0.7, 0.7)
                    }
                }
            ]
        };
    }

    private static ProjectDocument ShapeDocument()
    {
        var tracks = Enumerable.Range(0, 3).Select(index => new ProjectTrack { Name = $"Shape {index + 1}" }).ToArray();
        return new()
        {
            Tracks = [ProjectTrack.Default, .. tracks],
            Layers =
            [
                new()
                {
                    Kind = LayerKind.SHAPE, TrackId = tracks[0].Id, Shape = new(ShapeKind.RECTANGLE, 100, 60) { CornerRadii = new(2, 3, 4, 5) },
                    StrokeWidth = 1, Fill = new(2, 0.4, -0.1, 0.8), Stroke = new(0.1, 0.2, 0.3, 0.9)
                },
                new()
                {
                    Kind = LayerKind.SHAPE, TrackId = tracks[1].Id, Shape = new(ShapeKind.RECTANGLE, 80, 90) { CornerRadii = new(20, 30, 40, 50) },
                    StrokeWidth = 2, Fill = new(0.3, 4, 0.6, 0.7), Stroke = new(0.2, 0.3, 0.4, 0.6)
                },
                new()
                {
                    Kind = LayerKind.SHAPE, TrackId = tracks[2].Id, Shape = new(ShapeKind.ELLIPSE, 40, 30),
                    StrokeWidth = 3, Fill = new(0.4, 0.5, 0.6, 0.5), Stroke = new(0.3, 0.4, 0.5, 0.7)
                }
            ]
        };
    }
}
