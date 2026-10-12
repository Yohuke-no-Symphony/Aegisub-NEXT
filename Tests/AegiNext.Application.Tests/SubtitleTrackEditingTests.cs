using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTrackEditingTests
{
    [Fact]
    public void TrackCrudAndDisplayReorderingPreserveCompositionAndUndoHistory()
    {
        var editor = new ProjectEditor();
        var cue = editor.AddSubtitle(new(0), new(2), "one");
        var layer = Assert.Single(editor.Snapshot.Layers);
        var track = editor.AddTrack("Second");
        editor.RenameTrack(track, "Renamed");
        editor.MoveTrack(track, 0);
        Assert.Equal(track, editor.Snapshot.Tracks[0].Id);
        Assert.Same(layer, Assert.Single(editor.Snapshot.Layers));
        Assert.Equal(cue, Assert.Single(editor.Snapshot.Subtitles).Id);
        Assert.True(editor.Undo());
        Assert.Equal(track, editor.Snapshot.Tracks[1].Id);
        Assert.True(editor.Undo());
        Assert.Equal("Second", editor.Snapshot.Tracks[1].Name);
        Assert.True(editor.Redo());
        var populated = editor.Snapshot;
        editor.RemoveTrack(ProjectTrack.DEFAULT_TRACK_ID);
        Assert.Equal(track, Assert.Single(editor.Snapshot.Tracks).Id);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Empty(editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(populated, editor.Snapshot);
        editor.RemoveTrack(track);
        Assert.Single(editor.Snapshot.Tracks);
        var before = editor.Snapshot;
        editor.RemoveTrack(ProjectTrack.DEFAULT_TRACK_ID);
        Assert.Empty(editor.Snapshot.Tracks);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void CrossTrackTransferPreservesAllEffectsAndCollisionsAreAtomic()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "first");
        var secondTrack = editor.AddTrack("Second");
        var second = editor.AddSubtitle(new(0), new(2), "second", secondTrack);
        editor.UpdateLayer(second, layer => layer with
        {
            Transform = new(25, 30, 2, 3, 20), Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])],
            MotionPath = new(new(new(0, 0), [new(new(1, 0), new(2, 0), new(3, 0))]), new(2))
        });
        var before = editor.Snapshot;
        var secondLayer = before.Layers[1];
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        Assert.Throws<InvalidDataException>(() => editor.MoveSubtitleToTrack(second, ProjectTrack.DEFAULT_TRACK_ID));
        Assert.Same(before, editor.Snapshot);
        Assert.Equal(0, changes);

        editor.MoveSubtitleClip(second, ProjectTrack.DEFAULT_TRACK_ID, new(2), new(4), TimelineEditMode.CROP, move: true);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, new ProjectClipIndex(editor.Snapshot).GetSubtitleTrackId(editor.Snapshot.Subtitles[1].Id));
        var moved = editor.Snapshot.Layers[1];
        Assert.Equal(secondLayer.Id, moved.Id);
        Assert.Equal(secondLayer.Transform, moved.Transform);
        Assert.Equal(secondLayer.Tracks, moved.Tracks);
        Assert.Equal(secondLayer.MotionPath, moved.MotionPath);
        Assert.Equal(secondLayer.AnimationOffset, moved.AnimationOffset);
        Assert.Equal(before.Layers[0], editor.Snapshot.Layers[0]);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void CropKeepsContentPhaseAndRejectsNeighbourCollisionWithoutPublishing()
    {
        var editor = new ProjectEditor();
        var cue = editor.AddSubtitle(new(0), new(4), "crop");
        editor.AddSubtitle(new(4), new(6), "next");
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(0), 0));
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(4), 1));
        editor.MoveSubtitleClip(cue, ProjectTrack.DEFAULT_TRACK_ID, new(1), new(4), TimelineEditMode.CROP, move: false);
        var cropped = editor.Snapshot;
        var layer = cropped.Layers[0];
        Assert.Equal(new MediaTime(1), layer.AnimationOffset);
        Assert.Equal(new MediaTime(1), layer.Tracks[0].Keyframes[0].Time);
        Assert.Equal(0.25, layer.Tracks[0].Keyframes[0].Value.Scalar);

        Assert.Throws<InvalidDataException>(() => editor.SetSubtitleTiming(cue, new(1), new(5), TimelineEditMode.CROP));
        Assert.Same(cropped, editor.Snapshot);
        Assert.Throws<InvalidDataException>(() => editor.ShiftSubtitle(cue, new(1)));
        Assert.Same(cropped, editor.Snapshot);
    }

    [Fact]
    public void SplitStaysOnTrackAndMergeRejectsDifferentTracks()
    {
        var editor = new ProjectEditor();
        var track = editor.AddTrack("Second");
        var cue = editor.AddSubtitle(new(0), new(4), "abcd", track);
        editor.Apply("Split", document => ProjectEditingOperations.SplitSubtitle(document, cue, new(2), 2));
        Assert.All(editor.Snapshot.Layers, clip => Assert.Equal(track, clip.TrackId));
        var second = editor.Snapshot.Subtitles[1].Id;
        editor.MoveSubtitleToTrack(second, ProjectTrack.DEFAULT_TRACK_ID);
        var before = editor.Snapshot;
        Assert.Throws<InvalidOperationException>(() => editor.Apply("Merge", document =>
            ProjectEditingOperations.MergeSubtitles(document, cue, second, "")));
        Assert.Same(before, editor.Snapshot);
        editor.MoveSubtitleToTrack(second, track);
        editor.Apply("Merge", document => ProjectEditingOperations.MergeSubtitles(document, cue, second, ""));
        Assert.Equal("abcd", Assert.Single(editor.Snapshot.Subtitles).Text);
        Assert.Equal(track, Assert.Single(editor.Snapshot.Layers).TrackId);
        Assert.Equal(cue, Assert.Single(editor.Snapshot.Layers).Id);
    }

    [Fact]
    public void BatchImportRejectsInternalAndExistingCollisionsWithoutPartialHistory()
    {
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        var lines = new[]
        {
            new SubtitleLine { Start = new(0), End = new(2), Text = "one" },
            new SubtitleLine { Start = new(1), End = new(3), Text = "two" }
        };
        Assert.Throws<InvalidDataException>(() => editor.AddSubtitles(lines, ProjectTrack.DEFAULT_TRACK_ID));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        editor.AddSubtitle(new(0), new(2), "existing");
        before = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.AddSubtitles([lines[1]], ProjectTrack.DEFAULT_TRACK_ID));
        Assert.Same(before, editor.Snapshot);
        var track = editor.AddTrack("Second");
        editor.AddSubtitles([lines[1]], track);
        Assert.Equal(track, new ProjectClipIndex(editor.Snapshot).GetSubtitleTrackId(editor.Snapshot.Subtitles[1].Id));
        Assert.Equal(2, editor.Snapshot.Layers.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PreviousProjectVersionsAreExplicitlyRejected(int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()))!.AsObject();
        root["version"] = version;
        var error = Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Contains("Only project versions 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionThreeRoundTripsTracksAndRejectsMissingTrackFields()
    {
        var editor = new ProjectEditor();
        var track = editor.AddTrack("Second");
        var cue = editor.AddSubtitle(new(1, 3), new(4, 3), "precise", track);
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.Equal(track, Assert.Single(restored.Layers).TrackId);
        Assert.Equal(cue, Assert.Single(restored.Layers).Id);

        var root = JsonNode.Parse(bytes)!.AsObject();
        root.Remove("tracks");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        root = JsonNode.Parse(bytes)!.AsObject();
        root["layers"]![0]!.AsObject().Remove("trackId");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        root = JsonNode.Parse(bytes)!.AsObject();
        root["tracks"]![0]!.AsObject().Remove("name");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
