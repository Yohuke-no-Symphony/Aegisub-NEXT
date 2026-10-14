using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipClipboardEditingTests
{
    [Fact]
    public void CaptureUsesCompositionOrderAndFreezesContentWithoutChangingTheSource()
    {
        var document = CreateDocument();
        var selected = document.Layers.Reverse().Select(layer => layer.Id).ToList();
        selected.Add(selected[0]);
        var primary = document.Layers[1].Id;

        var content = ProjectEditingOperations.CaptureClips(document, selected, primary);
        selected.Clear();

        Assert.Equal(document.Id, content.SourceProjectId);
        Assert.Equal(primary, content.PrimaryId);
        Assert.Equal(MediaTime.Zero, content.EarliestStart);
        Assert.Equal<ProjectLayer>(document.Layers, content.Layers);
        Assert.Equal<SubtitleLine>(document.Subtitles, content.Subtitles);
        Assert.Same(document.Layers[0], content.Layers[0]);
        Assert.Same(document.Subtitles[0], content.Subtitles[0]);
    }

    [Fact]
    public void PasteRemapsAllInternalIdentitiesAndPreservesResourcesEffectsAndRelativeTime()
    {
        var document = CreateDocument();
        var originalLayer = document.Layers[1];
        var content = ProjectEditingOperations.CaptureClips(document, document.Layers.Select(layer => layer.Id).ToArray(), originalLayer.Id);
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        var result = editor.PasteClips(content, new(10));

        Assert.Same(editor.Snapshot, result.Document);
        Assert.Equal(1, changes);
        Assert.Equal(3, result.LayerIds.Length);
        Assert.Equal(result.LayerIds[1], result.PrimaryId);
        Assert.Equal(document.Layers, editor.Snapshot.Layers.Take(3));
        Assert.Equal(result.LayerIds, editor.Snapshot.Layers.Skip(3).Select(layer => layer.Id));
        Assert.Equal(document.Assets, editor.Snapshot.Assets);
        Assert.Equal(document.Tracks, editor.Snapshot.Tracks);
        for (var index = 0; index < 3; index++)
        {
            var source = document.Layers[index];
            var copy = editor.Snapshot.Layers[index + 3];
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal(source.Start + new MediaTime(10), copy.Start);
            Assert.Equal(source.End + new MediaTime(10), copy.End);
            Assert.Equal(source.AnimationOffset, copy.AnimationOffset);
            Assert.Equal(source.Transform, copy.Transform);
            Assert.Equal(source.MotionPath, copy.MotionPath);
            Assert.Equal(source.Shape, copy.Shape);
            Assert.Equal(source.Image, copy.Image);
        }
        var pastedLayer = editor.Snapshot.Layers[4];
        var originalLine = Assert.Single(document.Subtitles);
        var pastedLine = editor.Snapshot.Subtitles[1];
        Assert.NotEqual(originalLine.Id, pastedLine.Id);
        Assert.Equal(pastedLine.Id, pastedLayer.SubtitleId);
        Assert.Equal(pastedLine.Id, pastedLayer.Id);
        Assert.Equal(originalLine.Style, pastedLine.Style);
        Assert.Equal(originalLine.InlineSpans, pastedLine.InlineSpans);
        Assert.Equal(originalLayer.TrackId, pastedLayer.TrackId);
        Assert.NotEqual(originalLine.Karaoke[0].Id, pastedLine.Karaoke[0].Id);
        Assert.NotEqual(originalLine.InactiveKaraoke[0].Id, pastedLine.InactiveKaraoke[0].Id);
        Assert.Equal(originalLine.Karaoke[0] with { Id = pastedLine.Karaoke[0].Id }, pastedLine.Karaoke[0]);
        Assert.Equal(originalLine.InactiveKaraoke[0] with { Id = pastedLine.InactiveKaraoke[0].Id }, pastedLine.InactiveKaraoke[0]);
        var sourceMask = Assert.IsType<VectorClipMask>(originalLayer.Mask);
        var copyMask = Assert.IsType<VectorClipMask>(pastedLayer.Mask);
        Assert.NotEqual(sourceMask.Contours[0].Id, copyMask.Contours[0].Id);
        Assert.Equal(sourceMask.Transform, copyMask.Transform);
        Assert.Equal(sourceMask.Inverted, copyMask.Inverted);
        for (var index = 0; index < sourceMask.Contours[0].Nodes.Length; index++)
        {
            var source = sourceMask.Contours[0].Nodes[index];
            var copy = copyMask.Contours[0].Nodes[index];
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal(source with { Id = copy.Id }, copy);
        }
        Assert.Equal(copyMask.Contours[0].Nodes[0].Id, pastedLayer.Tracks[0].Target.NodeId);
        Assert.Equal(originalLayer.Tracks[0].Keyframes, pastedLayer.Tracks[0].Keyframes);
        var sourceOperation = Assert.Single(originalLayer.Tracks[1].Transforms);
        var copyOperation = Assert.Single(pastedLayer.Tracks[1].Transforms);
        Assert.NotEqual(sourceOperation.Id, copyOperation.Id);
        Assert.Equal(sourceOperation with { Id = copyOperation.Id }, copyOperation);
        Assert.Equal(originalLayer.Tracks[1].InitialValue, pastedLayer.Tracks[1].InitialValue);
        var after = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
        var reopened = ProjectStore.Deserialize(ProjectStore.Serialize(after));
        Assert.Equal(result.LayerIds, reopened.Layers.Skip(3).Select(layer => layer.Id));
        Assert.Equal(copyMask.Contours[0].Nodes.Select(node => node.Id),
            Assert.IsType<VectorClipMask>(reopened.Layers[4].Mask).Contours[0].Nodes.Select(node => node.Id));
    }

    [Fact]
    public void FrozenCopySurvivesSourceEditsAndDeletionAndEachPasteGetsNewIds()
    {
        var document = CreateDocument();
        var original = document.Layers[1];
        var content = ProjectEditingOperations.CaptureClips(document, [original.Id], original.Id);
        var editor = new ProjectEditor(document);
        editor.UpdateSubtitle(original.SubtitleId!.Value, line => line with { Text = "XY" });
        editor.RemoveClips([original.Id]);

        var first = editor.PasteClips(content, new(10));
        var second = editor.PasteClips(content, new(20));

        Assert.NotEqual(first.PrimaryId, second.PrimaryId);
        Assert.Equal("AB", editor.Snapshot.Subtitles[0].Text);
        Assert.Equal("AB", editor.Snapshot.Subtitles[1].Text);
        var firstLayer = editor.Snapshot.Layers.Single(layer => layer.Id == first.PrimaryId);
        var secondLayer = editor.Snapshot.Layers.Single(layer => layer.Id == second.PrimaryId);
        Assert.NotEqual(Assert.IsType<VectorClipMask>(firstLayer.Mask).Contours[0].Nodes[0].Id,
            Assert.IsType<VectorClipMask>(secondLayer.Mask).Contours[0].Nodes[0].Id);
        Assert.NotEqual(firstLayer.Tracks[1].Transforms[0].Id, secondLayer.Tracks[1].Transforms[0].Id);
        Assert.NotEqual(editor.Snapshot.Subtitles[0].Karaoke[0].Id, editor.Snapshot.Subtitles[1].Karaoke[0].Id);
    }

    [Fact]
    public void FinalSubtitleCollisionRejectsTheWholePasteAndRetainsRedo()
    {
        var document = CreateDocument();
        var content = ProjectEditingOperations.CaptureClips(document, document.Layers.Select(layer => layer.Id).ToArray(), document.Layers[1].Id);
        var editor = new ProjectEditor(document);
        editor.ShiftClips([document.Layers[0].Id], new(1));
        Assert.True(editor.Undo());
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content, MediaTime.Zero));

        Assert.Same(before, editor.Snapshot);
        Assert.Equal(0, changes);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void CaptureRejectsUnknownInvalidClipsEmptySelectionAndUnselectedPrimary()
    {
        var document = CreateDocument();
        var group = new ProjectLayer { Kind = (LayerKind)int.MaxValue };
        var withGroup = document with { Layers = document.Layers.Add(group) };
        var id = document.Layers[0].Id;

        Assert.Throws<KeyNotFoundException>(() => ProjectEditingOperations.CaptureClips(document, [id, Guid.NewGuid()], id));
        Assert.Throws<InvalidDataException>(() => ProjectEditingOperations.CaptureClips(withGroup, [id, group.Id], id));
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.CaptureClips(document, [], id));
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.CaptureClips(document, [id], document.Layers[1].Id));
    }

    [Fact]
    public void PasteRejectsAnotherProjectAndNegativePlacementWithoutPublishing()
    {
        var document = CreateDocument();
        var content = ProjectEditingOperations.CaptureClips(document, [document.Layers[0].Id], document.Layers[0].Id);
        var editor = new ProjectEditor(document with { Id = Guid.NewGuid() });
        var before = editor.Snapshot;

        Assert.Throws<InvalidOperationException>(() => editor.PasteClips(content, new(10)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        var source = new ProjectEditor(document);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.PasteClips(content, new(-1)));
        Assert.Same(document, source.Snapshot);
    }

    [Fact]
    public void PastePreservesMultipleTracksDistinctLayerIdentityAndExactFractionalOffsets()
    {
        var document = CreateDocument();
        var track = new ProjectTrack { Name = "Other" };
        var line = new SubtitleLine { Start = new(1), End = new(3), Text = "parallel" };
        var layer = new ProjectLayer { TrackId = track.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
        document = document with
        {
            Tracks = document.Tracks.Add(track),
            Subtitles = document.Subtitles.Add(line), Layers = document.Layers.Add(layer)
        };
        var content = ProjectEditingOperations.CaptureClips(document, document.Layers.Select(value => value.Id).ToArray(), layer.Id);
        var start = new MediaTime(1001, 30);

        var result = ProjectEditingOperations.PasteClips(document, content, start);

        Assert.Equal(start, result.Document.Layers.Skip(4).Min(clip => clip.Start));
        var copy = result.Document.Layers.Single(value => value.Id == result.PrimaryId);
        var copiedLine = result.Document.Subtitles.Single(value => value.Id == copy.SubtitleId);
        Assert.NotEqual(copy.Id, copiedLine.Id);
        Assert.NotEqual(layer.Id, copy.Id);
        Assert.NotEqual(line.Id, copiedLine.Id);
        Assert.Equal(track.Id, copy.TrackId);
        Assert.Equal(line.Start + start, copiedLine.Start);
        Assert.Equal(line.End + start, copiedLine.End);
        Assert.Equal(document.Layers[1].TrackId, new ProjectClipIndex(result.Document).GetSubtitleTrackId(result.Document.Subtitles[2].Id));
    }

    [Fact]
    public void MalformedClipboardAndMissingResourcesCannotReplaceTheSnapshot()
    {
        var document = CreateDocument();
        var shape = document.Layers[0];
        var content = ProjectEditingOperations.CaptureClips(document, [shape.Id], shape.Id);
        var editor = new ProjectEditor(document);

        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content with { EarliestStart = new(1) }, new(10)));
        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content with { PrimaryId = Guid.NewGuid() }, new(10)));
        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content with
        {
            Layers = [shape with { Kind = (LayerKind)int.MaxValue, Shape = null }]
        }, new(10)));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        var image = document.Layers[2];
        var imageContent = ProjectEditingOperations.CaptureClips(document, [image.Id], image.Id);
        var emptyResources = document with { Assets = [], Layers = [], Subtitles = [] };
        var missing = new ProjectEditor(emptyResources);
        Assert.Throws<InvalidDataException>(() => missing.PasteClips(imageContent, new(10)));
        Assert.Same(emptyResources, missing.Snapshot);
        Assert.False(missing.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/font.ttf");
        var inlineFont = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/inline.ttf");
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png");
        var line = new SubtitleLine
        {
            Start = new(1), End = new(3), Text = "AB",
            Style = new() { FontAssetId = font.Id },
            InlineSpans = [new(0, 1, new() { FontAssetId = inlineFont.Id })],
            Karaoke = [new(0, 1, new(1, 3), new(4, 3), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(4, 3), new(7, 3), SceneColor.White)]
        };
        var node = new MaskNode { Position = new(5, 6), InHandle = new(-1, 2), OutHandle = new(3, 4) };
        var subtitle = new ProjectLayer
        {
            Id = line.Id, SubtitleId = line.Id, Kind = LayerKind.SUBTITLE,
            Start = line.Start, End = line.End, AnimationOffset = new(1, 3),
            Transform = new(10, 20, 2, 3, 15),
            Mask = new VectorClipMask
            {
                Inverted = true, Contours = [new() { Nodes = [node, new() { Position = new(15, 16) }] }],
                Placement = new() { Pivot = new(0.5, 0.5), Offset = new(10, 11) },
                ReferenceFrame = new() { Origin = new(5, 6), Size = new(10, 10) },
                Transform = new() { Position = new(2, 3) }
            },
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), [new(new(1, 3), new ScenePoint(20, 30))]),
                new(AnimationProperty.OPACITY, [])
                {
                    InitialValue = 1,
                    Transforms = [new(Guid.NewGuid(), new(1, 3), new(7, 3), 0.25, 1.5)]
                }
            ],
            MotionPath = new(new(new(0, 0), [new(new(1, 2), new(3, 4), new(5, 6))]), new(2))
        };
        var shapeTrack = new ProjectTrack { Name = "Shape" };
        var shape = new ProjectLayer { TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Start = new(0), End = new(2), Shape = new(ShapeKind.ELLIPSE, 30, 40) };
        var image = new ProjectLayer { Kind = LayerKind.IMAGE, Start = new(4), End = new(6), Image = new(imageAsset.Id, 50, 60) };
        return new() { Tracks = [ProjectTrack.Default, shapeTrack], Assets = [font, inlineFont, imageAsset], Subtitles = [line], Layers = [shape, subtitle, image] };
    }
}
