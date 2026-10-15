using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class MaskEffectGestureEditingTests
{
    [Fact]
    public async Task VisibleGestureWritesTheInverseNativeKeyframeAndKeepsTheInstanceStackInOneUndo()
    {
        var document = Document();
        var track = new AnimationTrack(AnimationProperty.MASK_POSITION,
            [new(new(0), new ScenePoint(10, 20)), new(new(5), new ScenePoint(30, 40))]);
        var unrelated = new AnimationTrack(AnimationProperty.MASK_ROTATION, [new(new(0), 12d)]);
        var layer = document.Layers[0] with
        {
            Tracks = [track, unrelated],
            Effects = [Instance("mask-position factor(2, 3)"), Instance("mask-position offset(10, -5)")]
        };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.MaskEditing.BeginGesture());
        var visible = SceneEvaluator.EvaluateMask(layer, new(0), document.Subtitles[0])!;
        var draft = visible with { Transform = visible.Transform with { Position = new(50, 100) } };

        session.MaskEditing.CommitGesture(new(layer.Id, draft));

        var edited = context.Editor.Snapshot.Layers[0];
        Assert.Equal(new ScenePoint(20, 35), edited.Tracks[0].Keyframes[0].Value.Vector);
        Assert.Same(track.Keyframes[1], edited.Tracks[0].Keyframes[1]);
        Assert.Same(unrelated, edited.Tracks[1]);
        Assert.Equal(layer.Effects, edited.Effects);
        Assert.Equal(layer.Mask, edited.Mask);
        Assert.Equal(draft, SceneEvaluator.EvaluateMask(edited, new(0), document.Subtitles[0]));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CoveredTargetCannotCommitButIndependentGeometryRemainsEditable()
    {
        var document = Document();
        var layer = document.Layers[0] with { Effects = [Instance("mask-position (100, 200)")] };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var visible = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(0), document.Subtitles[0]));
        Assert.True(session.MaskEditing.BeginGesture());
        session.MaskEditing.CommitGesture(new(layer.Id, visible with { Transform = visible.Transform with { Position = new(120, 210) } }));
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(session.MaskEditing.BeginGesture());

        session.MaskEditing.CommitGesture(new(layer.Id, visible with { BottomRight = new(600, 400) }));

        var edited = context.Editor.Snapshot.Layers[0];
        Assert.Equal(layer.Mask!.Transform, edited.Mask!.Transform);
        Assert.Equal(new ScenePoint(600, 400), Assert.IsType<RectangleClipMask>(edited.Mask).BottomRight);
        Assert.Empty(edited.Tracks);
        Assert.Equal(layer.Effects, edited.Effects);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task AddingAContourPreservesExistingNativeNodesTransformAndTracksBeforeTheEffects()
    {
        var document = Document();
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [new() { Position = new(20, 30) }, new() { Position = new(100, 50) }] }],
            Transform = new() { Position = new(3, 4) }
        };
        var track = new AnimationTrack(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(90, 100))]);
        var layer = document.Layers[0] with { Mask = mask, Tracks = [track], Effects = [Instance("mask-position offset(10, 20)")] };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var visible = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(0), document.Subtitles[0]));
        var added = new MaskContour { Nodes = [new() { Position = new(300, 150) }] };
        var draft = visible with { Contours = visible.Contours.Add(added) };
        Assert.False(session.MaskEditing.IsTopologyLocked);
        Assert.True(session.MaskEditing.BeginGesture());

        session.MaskEditing.CommitGesture(new(layer.Id, draft, added.Nodes[0].Id));

        var edited = context.Editor.Snapshot.Layers[0];
        var editedMask = Assert.IsType<VectorClipMask>(edited.Mask);
        Assert.Equal(mask.Transform, editedMask.Transform);
        Assert.Same(mask.Contours[0], editedMask.Contours[0]);
        Assert.Same(added, editedMask.Contours[1]);
        Assert.Same(track, Assert.Single(edited.Tracks));
        Assert.Equal(layer.Effects, edited.Effects);
        Assert.Equal(draft, SceneEvaluator.EvaluateMask(edited, new(0), document.Subtitles[0]));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NodeInstanceChannelsUseTheSameTopologyLockAsCanvasCommands(bool enabled)
    {
        var document = Document();
        var node = new MaskNode { Position = new(20, 30) };
        var layer = document.Layers[0] with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [node, new() { Position = new(100, 50) }] }] },
            Effects = [Instance("mask-node(1,1).position offset(10, 20)") with { IsEnabled = enabled }]
        };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        session.MaskEditing.SelectNode(node.Id);

        Assert.Equal(enabled, session.MaskEditing.IsTopologyLocked);
        Assert.Equal(!enabled, session.MaskEditing.CanSubdivideSelectedNode);
        Assert.Equal(!enabled, session.MaskEditing.CanDeleteSelectedNode);
        Assert.Equal(!enabled, session.MaskEditing.CanDeleteSelectedContour);
        if (enabled)
        {
            Assert.True(session.MaskEditing.BeginGesture());
            session.MaskEditing.CommitSegmentInsertion(new(layer.Id, ((VectorClipMask)layer.Mask).Contours[0].Id, node.Id, 0.5));
            Assert.Same(document, context.Editor.Snapshot);
        }
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { Text = "Mask", End = new(5) };
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(500, 300) }
            }]
        };
    }

    private static EffectInstance Instance(string value) => new()
    {
        Name = "Mask gesture", End = new(5),
        Source = "effect \"mask-edit\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
            "at 0 " + value + "\nat 1 " + value + "\nend\nend"
    };
}
