using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineAnimationRowInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyFoldStateDoesNotExpandAnyEffectOrCreateUnsavedChanges(bool legacyCollapsed)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
        };
        var source = new ProjectDocument
        {
            Layers = [layer],
            TimelineViewState = new()
            {
                CollapsedAnimationRows = legacyCollapsed
                    ? [new(TimelineRowScope.TRACK, layer.TrackId, AnimationProperty.OPACITY)] : []
            }
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();

        Assert.Empty(context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Empty(context.Session.TimelineViewState.CollapsedAnimationRows);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewClipsStartCollapsedAndManualStateSurvivesEditsRemovalAndUndoRedo(bool collapsed)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20) };
        var document = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var opacity = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        var rotation = opacity with { Target = new(AnimationProperty.ROTATION) };

        context.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(0), 0.5));

        Assert.Empty(session.TimelineViewState.ExpandedAnimationClips);
        Assert.Empty(context.Editor.Snapshot.TimelineViewState.ExpandedAnimationClips);
        session.SetTimelineEffectClipCollapsed(opacity, collapsed);
        context.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(0), 0.75));
        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(opacity));
        context.Editor.SetKeyframe(layer.Id, AnimationProperty.ROTATION, new(new(0), 30));
        Assert.DoesNotContain(rotation, session.TimelineViewState.ExpandedAnimationClips);
        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(opacity));

        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(opacity));
        Assert.True(context.Editor.Redo());
        Assert.True(context.Editor.Redo());
        Assert.DoesNotContain(rotation, session.TimelineViewState.ExpandedAnimationClips);
        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(opacity));
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ExistingAndNewClipsStartCollapsedWithoutSynthesizingViewState()
    {
        var otherTrack = new ProjectTrack { Name = "Other" };
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var cue = new SubtitleLine { End = new(3), Text = "AB", AnimationRanges = [range] };
        var layer = new ProjectLayer
        {
            Id = cue.Id, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Tracks = [new(AnimationProperty.STROKE_WIDTH, [new(new(0), 0.5)])]
        };
        var otherLayer = new ProjectLayer
        {
            TrackId = otherTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20)
        };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, otherTrack], Subtitles = [cue], Layers = [layer, otherLayer]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var rangeTarget = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, TextRangeId: range.Id);
        var stateTarget = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE);

        context.Editor.SetKeyframe(layer.Id, AnimationProperty.STROKE_WIDTH, new(new(0), 0.75));
        context.Editor.SetKeyframe(otherLayer.Id, AnimationProperty.STROKE_WIDTH, new(new(0), 0.5));
        context.Editor.SetKeyframe(layer.Id, rangeTarget, new(new(0), 0.5));
        context.Editor.SetKeyframe(layer.Id, stateTarget, new(new(0), 0.5));

        var rows = context.Session.TimelineViewState.ExpandedAnimationClips;
        Assert.Empty(rows);
        Assert.Empty(context.Editor.Snapshot.TimelineViewState.ExpandedAnimationClips);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingAndReopeningPreservesNewRowStateWithoutMarkingTheProjectDirty(bool collapsed)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20) };
        await using var context = new WorkspaceSessionTestContext(new() { Layers = [layer] });
        await context.InitializeAsync();
        var session = context.Session;
        var row = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        context.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(0), 0.5));
        Assert.Empty(session.TimelineViewState.ExpandedAnimationClips);
        session.SetTimelineEffectClipCollapsed(row, collapsed);
        var content = context.Editor.Snapshot;
        var persisted = session.CreatePersistenceSnapshot(content);
        var path = Path.Combine(context.DirectoryPath, "new-animation.aeginext");
        await ProjectStore.SaveAsync(persisted, path);
        session.AcceptProjectSave(content, persisted);

        Assert.Equal(ProjectOpenStatus.OPENED, (await session.OpenProjectAsync(path)).Status);

        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(row));
        Assert.False(session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        context.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(0), 0.75));
        Assert.Equal(!collapsed, session.TimelineViewState.ExpandedAnimationClips.Contains(row));
        Assert.True(context.Editor.Undo());
        Assert.False(session.HasUnsavedChanges);
    }
}
