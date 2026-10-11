using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineEffectClipLifecycleTests
{
    [Fact]
    public async Task MoveRetainsExpansionPasteStartsCollapsedAndDeleteUndoRestoresTheOriginalIdentity()
    {
        var document = CreateDocument();
        var layer = Assert.Single(document.Layers);
        var target = Assert.Single(layer.Tracks).Target;
        var id = new TimelineEffectClipId(layer.Id, target);
        var destination = document.Tracks[1];
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        session.SetTimelineEffectClipCollapsed(id, false);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(session.HasUnsavedChanges);

        await session.ViewModel.Timeline.CommitTimingAsync(new(layer.Id, layer.Start, new(10), new(14), TimelineEditMode.CROP, true)
        {
            TrackId = destination.Id, ExpectedDocument = document
        });

        Assert.Null(session.LastError);
        var moved = context.Editor.Snapshot;
        var movedLayer = Assert.Single(moved.Layers);
        Assert.Equal(layer.Id, movedLayer.Id);
        Assert.Equal(destination.Id, movedLayer.TrackId);
        Assert.Equal(new MediaTime(10), movedLayer.Start);
        Assert.Equal(target, Assert.Single(movedLayer.Tracks).Target);
        Assert.False(EffectClip(context, id).IsCollapsed);
        Assert.Equal(id, Assert.Single(session.TimelineViewState.ExpandedAnimationClips));

        await session.CopyTimelineClipsAsync(layer.Id, [layer.Id]);
        Assert.Same(moved, context.Editor.Snapshot);
        await session.PasteTimelineClipsAtTargetAsync(new(ProjectTrack.DEFAULT_TRACK_ID, null, new(20)), moved);

        Assert.Null(session.LastError);
        var pasted = context.Editor.Snapshot;
        var copy = Assert.Single(pasted.Layers, clip => clip.Id != layer.Id);
        var copiedTarget = Assert.Single(copy.Tracks).Target;
        var copiedId = new TimelineEffectClipId(copy.Id, copiedTarget);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, copy.TrackId);
        Assert.Equal(new MediaTime(20), copy.Start);
        Assert.Equal(target.Property, copiedTarget.Property);
        Assert.Equal(target.State, copiedTarget.State);
        Assert.NotEqual(target.TextRangeId, copiedTarget.TextRangeId);
        Assert.True(EffectClip(context, copiedId).IsCollapsed);
        Assert.False(EffectClip(context, id).IsCollapsed);
        Assert.Equal(id, Assert.Single(session.TimelineViewState.ExpandedAnimationClips));
        Assert.Empty(pasted.TimelineViewState.ExpandedAnimationClips);

        await session.DeleteTimelineClipsAsync([layer.Id], pasted);

        Assert.Null(session.LastError);
        Assert.Equal(copy.Id, Assert.Single(context.Editor.Snapshot.Layers).Id);
        Assert.Equal(id, Assert.Single(session.TimelineViewState.ExpandedAnimationClips));
        Assert.True(context.Editor.Undo());
        await session.WaitForProjectIdleAsync();
        Assert.Same(pasted, context.Editor.Snapshot);
        Assert.False(EffectClip(context, id).IsCollapsed);
        Assert.True(EffectClip(context, copiedId).IsCollapsed);
        Assert.True(context.Editor.Undo());
        await session.WaitForProjectIdleAsync();
        Assert.Same(moved, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        await session.WaitForProjectIdleAsync();
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(session.HasUnsavedChanges);
        Assert.False(EffectClip(context, id).IsCollapsed);

        session.SetTimelineEffectClipCollapsed(id, true);

        Assert.False(session.HasUnsavedChanges);
        Assert.True(context.Editor.CanRedo);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SplitKeepsTheOriginalHalfExpandedAndTheNewHalfCollapsedWithoutAddingViewUndo()
    {
        var document = CreateDocument();
        var layer = Assert.Single(document.Layers);
        var target = Assert.Single(layer.Tracks).Target;
        var id = new TimelineEffectClipId(layer.Id, target);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SetTimelineEffectClipCollapsed(id, false);
        Assert.False(context.Editor.CanUndo);

        context.Editor.Apply("Split subtitle", source => ProjectEditingOperations.SplitSubtitle(source, layer.SubtitleId!.Value, new(2), 2));
        await context.Session.WaitForProjectIdleAsync();

        var split = context.Editor.Snapshot;
        var originalHalf = Assert.Single(split.Layers, clip => clip.Id == layer.Id);
        var newHalf = Assert.Single(split.Layers, clip => clip.Id != layer.Id);
        var newTarget = Assert.Single(newHalf.Tracks).Target;
        Assert.Equal(new MediaTime(2), originalHalf.End);
        Assert.Equal(originalHalf.End, newHalf.Start);
        Assert.Equal(layer.End, newHalf.End);
        Assert.Equal(target, Assert.Single(originalHalf.Tracks).Target);
        Assert.NotEqual(target.TextRangeId, newTarget.TextRangeId);
        Assert.Equal(target.Property, newTarget.Property);
        Assert.Equal(target.State, newTarget.State);
        Assert.False(EffectClip(context, id).IsCollapsed);
        Assert.True(EffectClip(context, new(newHalf.Id, newTarget)).IsCollapsed);
        Assert.Equal(id, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Empty(split.TimelineViewState.ExpandedAnimationClips);

        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(EffectClip(context, id).IsCollapsed);
        Assert.Equal(id, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
    }

    private static TimelineEffectClip EffectClip(WorkspaceSessionTestContext context, TimelineEffectClipId id)
    {
        var document = context.Editor.Snapshot;
        var layout = TimelineEffectClipProjection.Create(document.Layers,
            document.Subtitles.ToDictionary(cue => cue.Id), context.Session.TimelineViewState.ExpandedAnimationClips.ToHashSet());
        return Assert.Single(layout.ForLayer(id.LayerId), block => block.Id == id);
    }

    private static ProjectDocument CreateDocument()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        var cue = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [range] };
        var target = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH,
            TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE);
        return new()
        {
            Tracks = [ProjectTrack.Default, new() { Name = "Destination" }],
            Subtitles = [cue],
            Layers =
            [
                new()
                {
                    Id = cue.Id, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
                    Tracks = [new(target, [new(new(0), 1), new(new(4), 3)])]
                }
            ]
        };
    }
}
