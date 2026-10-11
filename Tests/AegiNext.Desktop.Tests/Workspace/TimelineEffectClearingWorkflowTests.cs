using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineEffectClearingWorkflowTests
{
    [Fact]
    public async Task ClearingClipsPreservesSelectionAndHasOneUndo()
    {
        var first = Line(0);
        var second = Line(3);
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var ids = new[] { first.Id, second.Id };
        context.Session.SelectLayer(second.Id, ids);

        await context.Session.ClearTimelineClipAnimationTracksAsync(ids, document);

        Assert.All(context.Editor.Snapshot.Layers, layer => Assert.Empty(layer.Tracks));
        Assert.Equal(second.Id, context.Session.SelectedLayerId);
        Assert.Equal(ids.Order(), context.Session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task EffectClearingOnlyTouchesTheFrozenClipAndKeepsOtherTracks()
    {
        var other = new ProjectTrack { Name = "Other" };
        var first = Line(0);
        var second = Line(3);
        var outside = Line(6);
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, other], Subtitles = [first, second, outside],
            Layers = [Layer(first), Layer(second), Layer(outside) with { TrackId = other.Id }]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(first.Id, [first.Id]);
        var effect = new TimelineEffectClipId(first.Id, new(AnimationProperty.OPACITY));

        await context.Session.ClearTimelineEffectClipAsync(effect, document);

        Assert.Equal(AnimationProperty.ROTATION, Assert.Single(context.Editor.Snapshot.Layers[0].Tracks).Property);
        Assert.Same(document.Layers[1], context.Editor.Snapshot.Layers[1]);
        Assert.Same(document.Layers[2], context.Editor.Snapshot.Layers[2]);
        Assert.Equal(first.Id, Assert.Single(context.Session.ViewModel.Timeline.SelectedLayerIds));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ClipMenuUsesFrozenIdsWhenSelectionChangesWithoutAnEdit()
    {
        var first = Line(0);
        var second = Line(3);
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(first.Id, [first.Id]);
        session.ViewModel.Timeline.SetClipContext(new(ProjectTrack.DEFAULT_TRACK_ID, first.Id, first.Start));
        session.SelectLayer(second.Id, [second.Id]);

        await session.ViewModel.Timeline.ClearClipAnimationTracksCommand.ExecuteAsync(null);

        Assert.Empty(context.Editor.Snapshot.Layers[0].Tracks);
        Assert.Same(document.Layers[1], context.Editor.Snapshot.Layers[1]);
        Assert.Equal(second.Id, session.SelectedLayerId);
        Assert.Equal(second.Id, Assert.Single(session.ViewModel.Timeline.SelectedLayerIds));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task EffectContextOnlyClearsTheExactMaskNodeOnTheFrozenClip()
    {
        var first = Line(0);
        var second = Line(3);
        var firstNode = new MaskNode();
        var secondNode = new MaskNode();
        var hit = Layer(second) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [firstNode, secondNode] }] },
            Tracks =
            [
                new(AnimationProperty.ROTATION, [new(new(0), 30)]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, firstNode.Id), [new(new(0), new ScenePoint(10, 20))]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, secondNode.Id), [])
                {
                    InitialValue = new ScenePoint(0, 0), Transforms = [new(Guid.NewGuid(), new(0), new(2), new ScenePoint(30, 40))]
                }
            ]
        };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), hit] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(first.Id, [first.Id]);
        context.Session.ViewModel.Timeline.SetEffectClipContext(new(
            new(second.Id, new(AnimationProperty.MASK_NODE_POSITION, firstNode.Id))));
        context.Session.SelectLayer(first.Id, [first.Id, second.Id]);

        await context.Session.ViewModel.Timeline.ClearEffectClipCommand.ExecuteAsync(null);

        var cleared = context.Editor.Snapshot;
        Assert.Same(document.Layers[0], cleared.Layers[0]);
        Assert.Equal(2, cleared.Layers[1].Tracks.Length);
        Assert.Contains(cleared.Layers[1].Tracks, track => track.Target == new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, secondNode.Id));
        Assert.DoesNotContain(cleared.Layers[1].Tracks, track => track.Target == new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, firstNode.Id));
        Assert.Same(hit.Mask, cleared.Layers[1].Mask);
        Assert.Same(hit.Transform, cleared.Layers[1].Transform);
        Assert.Equal(first.Id, context.Session.SelectedLayerId);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.Session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(cleared, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ClipPropertyClearingRejectsUnknownIdAndDoesNotAddHistoryForUnaffectedProperty()
    {
        var line = Line(0);
        var document = new ProjectDocument { Subtitles = [line], Layers = [Layer(line)] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        await context.Session.ClearTimelineEffectClipAsync(new(Guid.NewGuid(), new(AnimationProperty.OPACITY)), document);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.IsType<KeyNotFoundException>(context.Session.LastError);

        await context.Session.ClearTimelineEffectClipAsync(new(line.Id, new(AnimationProperty.BLUR)), document);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidDraftAndStaleContextsCannotClearAnimation()
    {
        var line = Line(0);
        var document = new ProjectDocument { Subtitles = [line], Layers = [Layer(line)] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(line.Id, [line.Id]);
        var input = context.Session.ViewModel.Subtitles.Rows[0];
        input.StartText = "invalid";
        await context.Session.ClearTimelineClipAnimationTracksAsync([line.Id], document);
        await context.Session.ClearTimelineEffectClipAsync(new(line.Id, new(AnimationProperty.OPACITY)), document);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("invalid", input.StartText);
        input.Accept(line);
        context.Editor.UpdateLayer(line.Id, value => value with { Name = "Changed" });
        var changed = context.Editor.Snapshot;

        await context.Session.ClearTimelineClipAnimationTracksAsync([line.Id], document);
        await context.Session.ClearTimelineEffectClipAsync(new(line.Id, new(AnimationProperty.OPACITY)), document);


        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task UnknownEffectClipDoesNotModifySnapshotOrHistory()
    {
        var line = Line(0);
        var document = new ProjectDocument { Subtitles = [line], Layers = [Layer(line)] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        await context.Session.ClearTimelineEffectClipAsync(new(Guid.NewGuid(), new(AnimationProperty.OPACITY)), document);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.IsType<KeyNotFoundException>(context.Session.LastError);
    }

    private static SubtitleLine Line(int start) => new() { Start = new(start), End = new(start + 2), Text = "Clip" };

    private static ProjectLayer Layer(SubtitleLine line) => new()
    {
        Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
        Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)]), new(AnimationProperty.ROTATION, [new(new(0), 30)])]
    };
}
