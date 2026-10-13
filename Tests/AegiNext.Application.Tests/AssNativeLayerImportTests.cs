using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssNativeLayerImportTests
{
    [Fact]
    public void PureGraphicsImportAsOneUndoableBatchWithoutSubtitlePayloads()
    {
        var editor = new ProjectEditor();
        var original = editor.Snapshot;
        var first = Shape(MediaTime.Zero, new(2));
        var second = Shape(MediaTime.Zero, new(2));
        var imported = new AssImportResult([], []) { Layers = [first, second] };

        editor.ImportSubtitleLines(imported, "Drawing");

        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Equal([first.Id, second.Id], new ProjectClipIndex(editor.Snapshot).LayersInDrawingOrder.Select(layer => layer.Id));
        Assert.NotEqual(editor.Snapshot.Layers[0].TrackId, editor.Snapshot.Layers[1].TrackId);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Snapshot.Layers.Length);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal([first.Id, second.Id], restored.Layers.Select(layer => layer.Id));
        Assert.Empty(restored.Subtitles);
    }

    [Fact]
    public void MixedGraphicsAndTextRetainSourceDrawingOrderAndAtomicClipProperties()
    {
        var line = new SubtitleLine { Text = "Text", End = new(2) };
        var mask = new RectangleClipMask { BottomRight = new(100, 100) };
        var transform = new LayerTransform { Position = new(10, 20) };
        ImmutableArray<AnimationTrack> tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.5), new(new(2), 1)])];
        var textLayer = new ProjectLayer
        {
            Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = mask, Transform = transform, Tracks = tracks
        };
        var first = Shape(MediaTime.Zero, new(2));
        var last = Shape(new(1), new(3));
        var source = new AssImportResult([line], [])
        {
            Clips = [new(line, mask, tracks) { Transform = transform }],
            Layers = [first, textLayer, last]
        };

        var imported = ProjectEditingOperations.ImportSubtitleLines(new(), source, "ASS");

        Assert.Equal([first.Id, textLayer.Id, last.Id], new ProjectClipIndex(imported).LayersInDrawingOrder.Select(layer => layer.Id));
        var actual = new ProjectClipIndex(imported).GetSubtitleClip(line.Id);
        Assert.Same(mask, actual.Mask);
        Assert.Same(transform, actual.Transform);
        Assert.Equal(tracks, actual.Tracks);
        Assert.Same(line, Assert.Single(imported.Subtitles));
    }

    [Fact]
    public void TimelineMappingShiftsAllNativeIntervalsAndPreservesRelativeAnimationClocks()
    {
        var line = new SubtitleLine { Text = "Text", Start = new(1), End = new(3) };
        var shape = Shape(new(1), new(3));
        ImmutableArray<AnimationTrack> tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0), new(new(1), 1)])];
        var clip = new SubtitleClipImport(line, null, tracks, new(1, 2));
        var subtitle = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Tracks = tracks, AnimationOffset = clip.ContentOffset
        };
        var source = new AssImportResult([line], []) { Clips = [clip], Layers = [shape, subtitle] };

        var shifted = SubtitleTimelineExchange.ToProjectTime(source, new(new(2)));
        var result = ProjectEditingOperations.ImportSubtitleLines(new(), shifted, "Mapped");

        Assert.All(result.Layers, layer => Assert.Equal((new MediaTime(-1), new MediaTime(1)), (layer.Start, layer.End)));
        Assert.Equal(clip.ContentOffset, result.Layers[1].AnimationOffset);
        Assert.Equal(tracks, result.Layers[1].Tracks);
        Assert.Equal(line with { Start = new(-1), End = new(1) }, Assert.Single(result.Subtitles));
        Assert.Equal(shape.Start, source.Layers[0].Start);
    }

    [Fact]
    public void ConflictingLegacyAndNativeClipDataFailsWithoutHistoryOrPartialImport()
    {
        var editor = new ProjectEditor();
        var original = editor.Snapshot;
        var line = new SubtitleLine { Text = "Text", End = new(2) };
        var source = new AssImportResult([line], [])
        {
            Clips = [new(line, null, [])],
            Layers = [Shape(MediaTime.Zero, new(2)), new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Transform = new(10, 20)
            }]
        };

        Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines(source, "ASS"));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ExpandedLayerBudgetIsCheckedBeforeAllocatingTracks()
    {
        var editor = new ProjectEditor();
        var original = editor.Snapshot;
        var source = new AssImportResult([], [])
        {
            Layers = Enumerable.Range(0, 10001).Select(_ => Shape(MediaTime.Zero, new(1))).ToImmutableArray()
        };

        var error = Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines(source, "ASS"));

        Assert.Contains("budget", error.Message, StringComparison.Ordinal);
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("clip")]
    [InlineData("layer")]
    public void MalformedNullBatchMembersFailValidationBeforeConstructingOrEditingLayers(string member)
    {
        var editor = new ProjectEditor();
        var original = editor.Snapshot;
        var line = new SubtitleLine { Text = "Text", End = new(2) };
        var imported = member switch
        {
            "line" => new AssImportResult([null!], []),
            "clip" => new AssImportResult([line], []) { Clips = [null!] },
            _ => new AssImportResult([line], []) { Layers = [null!] }
        };

        Assert.Throws<InvalidDataException>(() => editor.ImportSubtitleLines(imported, "ASS"));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void NonOverlappingImportedShapesShareATrack()
    {
        var first = Shape(MediaTime.Zero, new(1));
        var second = Shape(new(1), new(2));
        var imported = ProjectEditingOperations.ImportSubtitleLines(new(),
            new AssImportResult([], []) { Layers = [first, second] }, "Drawing");

        Assert.Equal(imported.Layers[0].TrackId, imported.Layers[1].TrackId);
    }

    private static ProjectLayer Shape(MediaTime start, MediaTime end)
    {
        return new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 30), Start = start, End = end };
    }
}
