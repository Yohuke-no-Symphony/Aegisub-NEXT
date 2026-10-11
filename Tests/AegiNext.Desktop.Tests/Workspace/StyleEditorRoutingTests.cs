using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleEditorRoutingTests
{
    [Fact]
    public async Task ImageDraftsCountWithoutASubtitleAndCommitWithOneUndo()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var image = document.Layers.Single(layer => layer.Kind == LayerKind.IMAGE);
        session.SelectLayer(image.Id, [image.Id]);
        var model = Assert.IsType<ImageStyleViewModel>(session.ViewModel.Styles.ActiveEditor);
        Assert.Null(session.SelectedCue);
        model.Width.RawText = "240";
        model.Height.RawText = "180";
        model.Fit = (int)ImageFit.FILL_CROP;

        Assert.True(session.HasProjectDrafts);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(session.TryCommitDrafts(false));
        Assert.False(session.HasProjectDrafts);
        Assert.Equal(new LayerImage(image.Image!.AssetId, 240, 180, ImageFit.FILL_CROP), session.SelectedLayer!.Image);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("120", model.Width.RawText);
    }

    [Fact]
    public async Task InvalidImageDraftKeepsTheActiveEditorUntilItsFieldIsRestored()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var image = document.Layers.Single(layer => layer.Kind == LayerKind.IMAGE);
        var shape = document.Layers.Single(layer => layer.Kind == LayerKind.SHAPE);
        session.SelectLayer(image.Id, [image.Id]);
        var model = session.ViewModel.Styles.Image;
        model.Width.RawText = "7e-";
        model.Height.RawText = "200";

        session.SelectLayer(shape.Id, [shape.Id]);

        Assert.Equal(image.Id, session.SelectedLayerId);
        Assert.Same(model, session.ViewModel.Styles.ActiveEditor);
        Assert.Equal("7e-", model.Width.RawText);
        Assert.Equal("styles", session.ViewModel.InvalidPanelId);
        Assert.Equal("LayerWidthInput", session.ViewModel.InvalidFieldKey);
        Assert.True(session.ViewModel.Styles.RestoreField("LayerWidthInput"));
        Assert.Equal("200", model.Height.RawText);
        session.SelectLayer(shape.Id, [shape.Id]);
        Assert.Same(session.ViewModel.Styles.Shape, session.ViewModel.Styles.ActiveEditor);
        Assert.Equal(200, context.Editor.Snapshot.Layers.Single(layer => layer.Id == image.Id).Image!.Height);
    }

    [Fact]
    public async Task HiddenSubtitleDraftsCannotChangeAnImageOrBlockItsCommit()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[0].Id);
        var image = document.Layers.Single(layer => layer.Kind == LayerKind.IMAGE);
        session.SelectLayer(image.Id, [image.Id]);
        session.ViewModel.Styles.Subtitle.FontSizeText = "invalid";
        session.ViewModel.Styles.Subtitle.FillDraft.HexText = "#broken";
        session.ViewModel.Styles.Subtitle.CommitBold(true);

        Assert.False(session.HasProjectDrafts);
        session.ViewModel.Styles.Image.Width.RawText = "250";
        Assert.True(session.TryCommitDrafts(false));
        Assert.Equal(document.Subtitles, context.Editor.Snapshot.Subtitles);
        Assert.Equal(250, session.SelectedLayer!.Image!.Width);
        session.SelectCue(document.Subtitles[0].Id);
        Assert.Equal("64", session.ViewModel.Styles.Subtitle.FontSizeText);
        Assert.False(session.HasProjectDrafts);
    }

    [Fact]
    public async Task MixedSelectionEditsOnlyThePrimaryShapeAndKeepsOtherClips()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var shape = document.Layers.Single(layer => layer.Kind == LayerKind.SHAPE);
        session.SelectLayer(shape.Id, document.Layers.Select(layer => layer.Id).ToArray());
        var model = Assert.IsType<ShapeStyleViewModel>(session.ViewModel.Styles.ActiveEditor);
        Assert.False(string.IsNullOrWhiteSpace(session.ViewModel.Styles.SelectionLabel));
        model.Width.RawText = "333";
        model.FillDraft.SetValue(new(0.25, 0.5, 0.75, 1));

        Assert.True(session.TryCommitDrafts(false));

        Assert.Equal(333, session.SelectedLayer!.Shape!.Width);
        Assert.Equal(new SceneColor(0.25, 0.5, 0.75, 1), session.SelectedLayer.Fill);
        Assert.Equal(document.Layers.Where(layer => layer.Id != shape.Id), context.Editor.Snapshot.Layers.Where(layer => layer.Id != shape.Id));
        Assert.Equal(document.Subtitles, context.Editor.Snapshot.Subtitles);
    }

    private static ProjectDocument Document()
    {
        var imageTrack = new ProjectTrack { Name = "Images" };
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var subtitle = new SubtitleLine { Text = "Caption", End = new(4) };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/fixture.png");
        return new()
        {
            Tracks = [ProjectTrack.Default, imageTrack, shapeTrack],
            Assets = [asset],
            Subtitles = [subtitle],
            Layers =
            [
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End },
                new() { TrackId = imageTrack.Id, Kind = LayerKind.IMAGE, Name = "Image", Image = new(asset.Id, 120, 80), End = new(4) },
                new() { TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Name = "Shape", Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(4) }
            ]
        };
    }
}
