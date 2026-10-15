using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineEffectClipProjectionTests
{
    [Fact]
    public void RangeArrayOrderGroupsEveryPropertyAndStateRegardlessOfTextPosition()
    {
        var later = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var hidden = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var earlier = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "ABCD", AnimationRanges = [later, hidden, earlier] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            Tracks =
            [
                Track(new(AnimationProperty.FILL, TextRangeId: earlier.Id)),
                Track(new(AnimationProperty.FILL, TextRangeId: later.Id, State: SubtitleAnimationState.INACTIVE)),
                Track(new(AnimationProperty.POSITION)),
                Track(new(AnimationProperty.FONT_SIZE, TextRangeId: later.Id)),
                Track(new(AnimationProperty.FILL, TextRangeId: later.Id, State: SubtitleAnimationState.ACTIVE)),
                Track(new(AnimationProperty.FILL, TextRangeId: later.Id))
            ]
        };
        var subtitles = new Dictionary<Guid, SubtitleLine> { [line.Id] = line };
        var expanded = new HashSet<TimelineEffectClipId> { new(layer.Id, new(AnimationProperty.FONT_SIZE, TextRangeId: later.Id)) };

        var first = TimelineEffectClipProjection.Create([layer], subtitles, expanded).ForLayer(layer.Id);
        subtitles[line.Id] = line with { AnimationRanges = [earlier, hidden, later] };
        var moved = TimelineEffectClipProjection.Create([layer], subtitles, expanded).ForLayer(layer.Id);

        Assert.Equal(new Guid?[] { null, later.Id, later.Id, later.Id, later.Id, earlier.Id },
            first.Select(block => block.Id.Target.TextRangeId));
        Assert.Equal(new Guid?[] { null, earlier.Id, later.Id, later.Id, later.Id, later.Id },
            moved.Select(block => block.Id.Target.TextRangeId));
        Assert.DoesNotContain(first, block => block.Id.Target.TextRangeId == hidden.Id);
        Assert.Equal(first.Select(block => block.Id).OrderBy(id => id.Target.ToString()),
            moved.Select(block => block.Id).OrderBy(id => id.Target.ToString()));
        Assert.Single(moved, block => !block.IsCollapsed);
        Assert.Equal(layer.Tracks[0].Target, new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: earlier.Id));
    }

    [Fact]
    public void ConsecutiveClipsShareTheMaximumStackHeightAndAlignAboveTheirOwnParent()
    {
        var first = new ProjectLayer
        {
            Start = new(0), End = new(2),
            Tracks = [Track(new(AnimationProperty.OPACITY)), Track(new(AnimationProperty.POSITION)), Track(new(AnimationProperty.FILL))]
        };
        var second = new ProjectLayer
        {
            Start = new(2), End = new(4), Tracks = [Track(new(AnimationProperty.OPACITY))]
        };
        var empty = new ProjectLayer { Start = new(4), End = new(6) };

        var layout = Project([first, second, empty]);

        Assert.Equal(120, layout.Height);
        var firstBlocks = layout.ForLayer(first.Id);
        Assert.Equal([AnimationProperty.POSITION, AnimationProperty.OPACITY, AnimationProperty.FILL],
            firstBlocks.Select(block => block.Id.Target.Property));
        Assert.Equal([80d, 40d, 0d], firstBlocks.Select(block => block.Top));
        Assert.All(firstBlocks, block => Assert.True(block.IsCollapsed));
        var secondBlock = Assert.Single(layout.ForLayer(second.Id));
        Assert.Equal(80, secondBlock.Top);
        Assert.Equal(layout.Height, secondBlock.Top + secondBlock.Height);
        Assert.Equal(layout.Height, firstBlocks[0].Top + firstBlocks[0].Height);
        Assert.Empty(layout.ForLayer(empty.Id));
        Assert.Empty(layout.ForLayer(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(AnimationProperty.OPACITY, 76)]
    [InlineData(AnimationProperty.FILL, 112)]
    public void ExpandingOneCompleteTargetDoesNotExpandAnotherClipOrItsSibling(AnimationProperty property, double expandedHeight)
    {
        var first = new ProjectLayer
        {
            Start = new(0), End = new(2), Tracks = [Track(new(AnimationProperty.OPACITY)), Track(new(AnimationProperty.FILL))]
        };
        var second = first with { Id = Guid.NewGuid(), Start = new(2), End = new(4) };
        var id = new TimelineEffectClipId(first.Id, new(property));

        var layout = Project([first, second], [id]);

        Assert.Equal(expandedHeight + 40, layout.Height);
        var expanded = Assert.Single(layout.ForLayer(first.Id), block => !block.IsCollapsed);
        Assert.Equal(id, expanded.Id);
        Assert.Equal(expandedHeight, expanded.Height);
        Assert.All(layout.ForLayer(second.Id), block => Assert.True(block.IsCollapsed));
        Assert.Equal(40, Assert.Single(layout.ForLayer(first.Id), block => block.Id != id).Height);
        var collapsed = Project([first, second]);
        Assert.Equal(80, collapsed.Height);
        Assert.All(collapsed.Layers.Values.SelectMany(blocks => blocks), block => Assert.True(block.IsCollapsed));
    }

    [Fact]
    public void FullTargetsKeepRangeStateAndMaskNodeIdentityAndStableDisplayOrder()
    {
        var firstRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var secondRange = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var line = new SubtitleLine { Text = "ABCD", AnimationRanges = [firstRange, secondRange] };
        var firstNode = new MaskNode { Position = new(1, 1) };
        var secondNode = new MaskNode { Position = new(2, 2) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            Mask = new VectorClipMask
            {
                Contours = [new() { Nodes = [firstNode] }, new() { Nodes = [secondNode] }]
            },
            Tracks =
            [
                Track(new(AnimationProperty.MASK_NODE_POSITION, secondNode.Id)),
                Track(new(AnimationProperty.FILL, TextRangeId: secondRange.Id)),
                Track(new(AnimationProperty.FILL, TextRangeId: firstRange.Id, State: SubtitleAnimationState.INACTIVE)),
                Track(new(AnimationProperty.FILL, TextRangeId: firstRange.Id, State: SubtitleAnimationState.ACTIVE)),
                Track(new(AnimationProperty.MASK_NODE_POSITION, firstNode.Id)),
                Track(new(AnimationProperty.FILL)),
                Track(new(AnimationProperty.FILL, TextRangeId: firstRange.Id))
            ]
        };
        var selected = new TimelineEffectClipId(layer.Id,
            new(AnimationProperty.FILL, TextRangeId: firstRange.Id, State: SubtitleAnimationState.ACTIVE));
        var subtitles = new Dictionary<Guid, SubtitleLine> { [line.Id] = line };

        var layout = TimelineEffectClipProjection.Create([layer], subtitles, new HashSet<TimelineEffectClipId> { selected });
        var blocks = layout.ForLayer(layer.Id);
        var reordered = TimelineEffectClipProjection.Create([layer with { Tracks = [.. layer.Tracks.Reverse()] }], subtitles,
            new HashSet<TimelineEffectClipId> { selected });

        Assert.Equal(7, blocks.Length);
        Assert.Equal(7, blocks.Select(block => block.Id).Distinct().Count());
        Assert.Equal(selected, Assert.Single(blocks, block => !block.IsCollapsed).Id);
        Assert.Equal(blocks.ToArray(), reordered.ForLayer(layer.Id).ToArray());
        var rangeBlocks = blocks.Where(block => block.Id.Target.TextRangeId == firstRange.Id).ToArray();
        Assert.Equal([SubtitleAnimationState.NORMAL, SubtitleAnimationState.ACTIVE, SubtitleAnimationState.INACTIVE],
            rangeBlocks.Select(block => block.Id.Target.State));
        Assert.All(rangeBlocks, block => Assert.Equal("1–2", block.RangeLabel));
        Assert.Equal("3–4", Assert.Single(blocks, block => block.Id.Target.TextRangeId == secondRange.Id).RangeLabel);
        var nodes = blocks.Where(block => block.Id.Target.NodeId.HasValue).ToArray();
        Assert.Equal([firstNode.Id, secondNode.Id], nodes.Select(block => block.Id.Target.NodeId!.Value));
        Assert.Equal([1, 2], nodes.Select(block => block.ContourNumber));
        Assert.All(nodes, block => Assert.Equal(1, block.NodeNumber));
    }

    [Fact]
    public void TenThousandMaskNodesProjectDeterministicallyWithoutMergingTheirTargets()
    {
        var nodes = Enumerable.Range(0, 10000).Select(index => new MaskNode { Position = new(index, index % 7) }).ToImmutableArray();
        var line = new SubtitleLine { Text = "Mask" };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            Mask = new VectorClipMask { Contours = [new() { Nodes = nodes }] },
            Tracks = [.. nodes.Reverse().Select(node => Track(new(AnimationProperty.MASK_NODE_POSITION, node.Id)))]
        };
        var expanded = new TimelineEffectClipId(layer.Id, new(AnimationProperty.MASK_NODE_POSITION, nodes[5000].Id));

        var first = Project([layer], [expanded]);
        var repeated = Project([layer with { Tracks = [.. layer.Tracks.Reverse()] }], [expanded]);

        Assert.Equal(10000, first.ForLayer(layer.Id).Length);
        Assert.Equal(400036, first.Height);
        Assert.Equal(first.Height, repeated.Height);
        Assert.Equal(first.ForLayer(layer.Id).ToArray(), repeated.ForLayer(layer.Id).ToArray());
        Assert.Equal(nodes.Select(node => node.Id), first.ForLayer(layer.Id).Select(block => block.Id.Target.NodeId!.Value));
        Assert.Equal(Enumerable.Range(1, 10000), first.ForLayer(layer.Id).Select(block => block.NodeNumber));
        Assert.Equal(expanded, Assert.Single(first.ForLayer(layer.Id), block => !block.IsCollapsed).Id);
        Assert.Equal(0, first.ForLayer(layer.Id)[^1].Top);
    }

    [Fact]
    public void StaticGraphicGeometryCropAndPlaybackDoNotCreateEffectBlocks()
    {
        var image = new ProjectLayer
        {
            Kind = LayerKind.IMAGE,
            Image = new(Guid.NewGuid(), 200, 100)
            {
                Crop = new(0.1, 0.2, 0.5, 0.5),
                Playback = new() { SourceOffset = new(7, 3), Speed = 1.5 }
            }
        };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.PATH, 40, 20, ShapeGeometry.CreatePath(new(ShapeKind.ARROW, 40, 20))),
            Fill = new(0.5, 0.4, 0.3, 1), StrokeWidth = 2
        };

        var staticLayout = Project([image, shape]);

        Assert.Equal(0, staticLayout.Height);
        Assert.Empty(staticLayout.ForLayer(image.Id));
        Assert.Empty(staticLayout.ForLayer(shape.Id));
        var animated = Project([image with { Tracks = [Track(new(AnimationProperty.OPACITY))] },
            shape with { Tracks = [Track(new(AnimationProperty.SCALE))] }]);
        Assert.Equal(40, animated.Height);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(animated.ForLayer(image.Id)).Id.Target.Property);
        Assert.Equal(AnimationProperty.SCALE, Assert.Single(animated.ForLayer(shape.Id)).Id.Target.Property);
    }

    [Fact]
    public void ReplacingImageAssetOrConvertingShapeGeometryPreservesEffectIdentityAndExpansion()
    {
        var image = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Image = new(Guid.NewGuid(), 200, 100), Tracks = [Track(new(AnimationProperty.OPACITY))]
        };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ROUNDED_RECTANGLE, 40, 20) { CornerRadius = 5 },
            Tracks = [Track(new(AnimationProperty.ROTATION))]
        };
        var expanded = new[]
        {
            new TimelineEffectClipId(image.Id, new(AnimationProperty.OPACITY)),
            new TimelineEffectClipId(shape.Id, new(AnimationProperty.ROTATION))
        };
        var original = Project([image, shape], expanded);
        var replacementImage = image with { Image = image.Image! with { AssetId = Guid.NewGuid(), Crop = new(0, 0, 0.5, 1) } };
        var replacementShape = shape with
        {
            Shape = new(ShapeKind.PATH, 80, 40, ShapeGeometry.CreatePath(new(ShapeKind.ELLIPSE, 80, 40)))
        };

        var changed = Project([replacementImage, replacementShape], expanded);

        Assert.Equal(original.Height, changed.Height);
        Assert.Equal(original.ForLayer(image.Id).ToArray(), changed.ForLayer(image.Id).ToArray());
        Assert.Equal(original.ForLayer(shape.Id).ToArray(), changed.ForLayer(shape.Id).ToArray());
        Assert.All(changed.Layers.Values.SelectMany(blocks => blocks), block => Assert.False(block.IsCollapsed));
    }

    [Fact]
    public void CropAndStretchKeepImageSourceClockSeparateFromTheSameEffectIdentity()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.IMAGE, Start = new(10), End = new(20), AnimationOffset = new(7),
            Image = new(Guid.NewGuid(), 100, 50) { Playback = new() { SourceOffset = new(3), Speed = 1.5 } },
            Tracks = [new(AnimationProperty.OPACITY, [new(new(7), 0), new(new(17), 1)])]
        };
        var expanded = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        var cropped = LayerAnimationTiming.Retime(layer, new(12), new(20), TimelineEditMode.CROP);
        var stretched = LayerAnimationTiming.Retime(layer, new(10), new(30), TimelineEditMode.STRETCH);
        var expected = Assert.Single(Project([layer], [expanded]).ForLayer(layer.Id));

        Assert.Equal(expected, Assert.Single(Project([cropped], [expanded]).ForLayer(layer.Id)));
        Assert.Equal(expected, Assert.Single(Project([stretched], [expanded]).ForLayer(layer.Id)));
        Assert.Equal(new MediaTime(6), cropped.Image!.Playback!.SourceOffset);
        Assert.Equal(ImagePlaybackTiming.GetSourceTime(layer, new(15)), ImagePlaybackTiming.GetSourceTime(cropped, new(15)));
        Assert.Equal(layer.Image!.Playback, stretched.Image!.Playback);
        Assert.Equal(new MediaTime(14), stretched.AnimationOffset);
        Assert.Equal(cropped.Start, cropped.Start + cropped.Tracks[0].Keyframes[0].Time - cropped.AnimationOffset);
        Assert.Equal(stretched.End, stretched.Start + stretched.Tracks[0].Keyframes[^1].Time - stretched.AnimationOffset);
    }

    [Fact]
    public void EmptyTracksAreExcludedWhileOrderedTransformsUseOneBlockForTheirCompleteTarget()
    {
        var target = new AnimationTrackTarget(AnimationProperty.OPACITY);
        var layer = new ProjectLayer
        {
            Tracks =
            [
                new(target, [])
                {
                    InitialValue = 0,
                    Transforms = [new(Guid.NewGuid(), new(0), new(1), 0.5), new(Guid.NewGuid(), new(1), new(2), 1)]
                },
                new(AnimationProperty.ROTATION, [])
            ]
        };

        var layout = Project([layer]);

        Assert.Equal(40, layout.Height);
        Assert.Equal(new TimelineEffectClipId(layer.Id, target), Assert.Single(layout.ForLayer(layer.Id)).Id);
        Assert.Equal(0, TimelineEffectClipLayout.Empty.Height);
        Assert.Empty(TimelineEffectClipLayout.Empty.ForLayer(layer.Id));
    }

    private static TimelineEffectClipLayout Project(IEnumerable<ProjectLayer> layers, IEnumerable<TimelineEffectClipId>? expanded = null)
    {
        return TimelineEffectClipProjection.Create(layers, new Dictionary<Guid, SubtitleLine>(),
            expanded?.ToHashSet() ?? new HashSet<TimelineEffectClipId>());
    }

    private static AnimationTrack Track(AnimationTrackTarget target)
    {
        var value = AnimationPropertyMetadata.GetValueKind(target.Property) switch
        {
            AnimationValueKind.SCALAR => AnimationValue.FromScalar(0.5),
            AnimationValueKind.VECTOR => AnimationValue.FromVector(new(1, 2)),
            AnimationValueKind.COLOR => AnimationValue.FromColor(SceneColor.White),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return new(target, [new(MediaTime.Zero, value)]);
    }
}
