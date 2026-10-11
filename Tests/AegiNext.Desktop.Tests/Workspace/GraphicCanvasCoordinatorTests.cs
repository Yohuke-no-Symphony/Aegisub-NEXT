using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class GraphicCanvasCoordinatorTests
{
    [Fact]
    public async Task ShapeCreationUsesTheCapturedSubtitleIntervalAndCreatesItsTrackInOneUndo()
    {
        var cue = new SubtitleLine { Text = "Background", Start = new(5), End = new(9) };
        var source = new ProjectDocument
        {
            Subtitles = [cue], Layers = [new() { SubtitleId = cue.Id, Start = cue.Start, End = cue.End }]
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(cue.Id);
        Assert.True(session.SetGraphicCanvasMode(CanvasEditMode.CREATE_SHAPE));
        Assert.True(session.Graphics.BeginCreation());

        session.Graphics.CreateShape(new(ShapeKind.RECTANGLE, new(80, 90), 220, 70));

        Assert.Null(session.LastError);
        var shape = Assert.Single(context.Editor.Snapshot.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Equal(cue.Start, shape.Start);
        Assert.Equal(cue.End, shape.End);
        Assert.Equal(new ScenePoint(80, 90), shape.Transform.Position);
        Assert.NotEqual(source.Layers[0].TrackId, shape.TrackId);
        Assert.Equal(shape.Id, session.SelectedLayerId);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CancelledOrStaleCreationDoesNotWriteToTheNewDocumentOrSelection()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        Assert.True(session.SetGraphicCanvasMode(CanvasEditMode.CREATE_SHAPE));
        Assert.True(session.Graphics.BeginCreation());
        session.Graphics.CancelCreation();
        var source = context.Editor.Snapshot;
        session.Graphics.CreateShape(new(ShapeKind.RECTANGLE, new(), 20, 20));
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(session.Graphics.BeginCreation());
        var replacement = new ProjectDocument { Name = "Replacement" };
        context.Editor.Reset(replacement);
        session.Graphics.CreateShape(new(ShapeKind.RECTANGLE, new(), 20, 20));
        Assert.Same(replacement, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CropGestureCommitsOnlyCropAndRejectsALateResultAfterUndo()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/test.png");
        var image = new LayerImage(asset.Id, 200, 100) { Playback = new() { Speed = 2 } };
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = image };
        var source = new ProjectDocument { Assets = [asset], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SetGraphicCanvasMode(CanvasEditMode.CROP));
        Assert.True(session.Graphics.BeginGesture());
        session.Graphics.CommitGesture(new(layer.Id, image with { Width = 999, Crop = new(0.1, 0.2, 0.5, 0.6), Playback = null }, null));
        Assert.Null(session.LastError);
        var changed = session.SelectedLayer!.Image!;
        Assert.Equal(new NormalizedImageCrop(0.1, 0.2, 0.5, 0.6), changed.Crop);
        Assert.Equal(image.Width, changed.Width);
        Assert.Equal(image.Playback, changed.Playback);
        Assert.True(session.Graphics.BeginGesture());
        Assert.True(context.Editor.Undo());
        session.Graphics.CommitGesture(new(layer.Id, image with { Crop = new(0, 0, 0.2, 0.2) }, null));
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SubtitleBackgroundIsIndependentBelowTheSubtitleAndUndoesTogether()
    {
        var cue = new SubtitleLine { Text = "Subtitle background", Start = new(1), End = new(5) };
        var subtitle = new ProjectLayer { SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var source = new ProjectDocument { Subtitles = [cue], Layers = [subtitle] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectCue(cue.Id);

        await context.Session.Graphics.CreateSubtitleBackgroundAsync();

        Assert.Null(context.Session.LastError);
        var document = context.Editor.Snapshot;
        var shape = Assert.Single(document.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Equal(ShapeKind.RECTANGLE, shape.Shape!.Kind);
        Assert.Equal(ShapeCornerRadii.Uniform(8), shape.Shape.CornerRadii);
        Assert.Equal(cue.Start, shape.Start);
        Assert.Equal(cue.End, shape.End);
        Assert.Equal(subtitle.TrackId, document.Tracks[0].Id);
        Assert.Equal(shape.TrackId, document.Tracks[1].Id);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task MatchTimingRequiresOneSelectedSubtitleAndPreservesImagePlayback()
    {
        var cue = new SubtitleLine { Text = "Timing", Start = new(3), End = new(7) };
        var subtitle = new ProjectLayer { SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var track = new ProjectTrack { Name = "Image" };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/test.png");
        var image = new ProjectLayer { TrackId = track.Id, Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100) { Playback = new() { Speed = 2, SourceOffset = new(2) } } };
        var source = new ProjectDocument { Tracks = [ProjectTrack.Default, track], Assets = [asset], Subtitles = [cue], Layers = [subtitle, image] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(image.Id, [image.Id]);
        Assert.False(context.Session.Graphics.CanMatchSubtitleTiming);
        context.Session.SelectLayer(image.Id, [image.Id, subtitle.Id]);
        Assert.True(context.Session.Graphics.CanMatchSubtitleTiming);
        await context.Session.Graphics.MatchSelectedSubtitleTimingAsync();
        Assert.Null(context.Session.LastError);
        Assert.Equal(cue.Start, context.Session.SelectedLayer!.Start);
        Assert.Equal(cue.End, context.Session.SelectedLayer.End);
        Assert.Equal(image.Image!.Playback, context.Session.SelectedLayer.Image!.Playback);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }
}
