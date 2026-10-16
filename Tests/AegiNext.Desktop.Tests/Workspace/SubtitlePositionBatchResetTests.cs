using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitlePositionBatchResetTests
{
    [Fact]
    public async Task AutomaticPositionCommandResetsEverySelectedSubtitleAndKeepsOtherContentWithOneUndo()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        var original = context.Editor.Snapshot;
        var changes = 0;
        context.Editor.Changed += (_, _) => changes++;

        await context.Session.ViewModel.Styles.Subtitle.RestoreAutomaticPositionAsync();

        var changed = context.Editor.Snapshot;
        for (var index = 0; index < 2; index++)
        {
            var line = original.Subtitles[index];
            Assert.Equal(line with { Style = line.Style with { Position = null } }, changed.Subtitles[index]);
            var layer = original.Layers[index];
            var reset = changed.Layers[index];
            Assert.Equal(layer with
            {
                Transform = layer.Transform with { Position = default }, MotionPath = null, Tracks = reset.Tracks
            }, reset);
            Assert.Equal(layer.Tracks.Where(track => track.Property is not (AnimationProperty.POSITION or AnimationProperty.PATH_PROGRESS)),
                reset.Tracks);
        }
        Assert.Same(original.Subtitles[2], changed.Subtitles[2]);
        Assert.Same(original.Layers[2], changed.Layers[2]);
        Assert.Equal(1, changes);
        Assert.False(context.Session.ViewModel.Styles.Subtitle.Position.IsExplicit);
        await context.Session.ViewModel.Styles.Subtitle.RestoreAutomaticPositionAsync();
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Equal(1, changes);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task MixedSelectionCannotResetThePrimarySubtitlesPositionOrTracks()
    {
        var document = Document();
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, TrackId = shapeTrack.Id, Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(4)
        };
        document = document with { Tracks = document.Tracks.Add(shapeTrack), Layers = document.Layers.Add(shape) };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id, shape.Id]);

        context.Session.ResetAutomaticPosition();
        await context.Session.ViewModel.Styles.Subtitle.RestoreAutomaticPositionAsync();

        Assert.Null(context.Session.ViewModel.Styles.ActiveEditor);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var lines = Enumerable.Range(0, 3).Select(index => new SubtitleLine
        {
            Text = "Subtitle " + index, Start = new(index * 5), End = new(index * 5 + 4),
            Style = new()
            {
                FontFamily = "sans-serif", FontSize = 40 + index * 10,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.3, 0.4), Offset = new(10 + index * 20, 40) }
            },
            InlineSpans = [new(0, 2, new() { Bold = true })],
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White)]
        }).ToArray();
        return new()
        {
            Subtitles = [.. lines],
            Layers = [.. lines.Select((line, index) => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
                Transform = new(30 + index * 10, 40 - index * 5, 1.5 + index, 0.75 + index, 25 + index),
                Opacity = 0.75 - index * 0.1,
                MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(4)),
                Tracks =
                [
                    new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(30 + index * 10, 40 - index * 5))]),
                    new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(new(4), 1)]),
                    new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(1.5 + index, 0.75 + index))]),
                    new(AnimationProperty.OPACITY, [new(new(0), 0.75 - index * 0.1)])
                ]
            })]
        };
    }
}
