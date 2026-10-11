using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class GraphicStyleDraftTests
{
    [Fact]
    public async Task LockedImageDimensionsAndPlaybackCropDraftsCommitTogetherAndUndoExactly()
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id]);
        var model = context.Session.ViewModel.Styles.Image;
        Assert.True(model.AspectRatioLocked);
        model.Width.RawText = "300";
        Assert.Equal("150", model.Height.RawText);
        model.CropX.RawText = "10";
        model.CropY.RawText = "20";
        model.CropWidth.RawText = "70";
        model.CropHeight.RawText = "60";
        model.AnimationEnabled = true;
        model.PlaybackOffset.RawText = "-0.5";
        model.PlaybackSpeed.RawText = "2";
        model.PlaybackMode = (int)ImagePlaybackMode.FILE;

        Assert.True(context.Session.TryCommitDrafts(false));

        var image = context.Session.SelectedLayer!.Image!;
        Assert.Equal(300, image.Width);
        Assert.Equal(150, image.Height);
        Assert.Equal(new NormalizedImageCrop(0.1, 0.2, 0.7, 0.6), image.Crop);
        Assert.Equal(new MediaTime(-1, 2), image.Playback!.SourceOffset);
        Assert.Equal(2, image.Playback.Speed);
        Assert.Equal(ImagePlaybackMode.FILE, image.Playback.Mode);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("200", model.Width.RawText);
        Assert.Equal("100", model.CropWidth.RawText);
    }

    [Fact]
    public async Task InvalidCropAndDimensionDraftsRemainEditableAndEscapeRestoresOnlyTheRequestedField()
    {
        var source = ImageDocument();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id]);
        var model = context.Session.ViewModel.Styles.Image;
        model.Width.RawText = "7e-";
        model.Height.RawText = "120";
        Assert.Equal("7e-", model.Width.RawText);
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.True(model.RestoreField("LayerWidthInput"));
        Assert.Equal("120", model.Height.RawText);
        model.CropX.RawText = "60";
        model.CropWidth.RawText = "50";
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("ImageCropWidthInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(model.RestoreField("ImageCropXInput"));
        Assert.Equal("50", model.CropWidth.RawText);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(0.5, context.Session.SelectedLayer!.Image!.Crop.Width);
        Assert.Equal(120, context.Session.SelectedLayer.Image.Height);
    }

    [Fact]
    public async Task DisablingPlaybackIgnoresItsHiddenInvalidDraftAndLanguageRefreshKeepsPendingFit()
    {
        var source = ImageDocument();
        source = source with { Layers = [source.Layers[0] with { Image = source.Layers[0].Image! with { Playback = new() } }] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id]);
        var model = context.Session.ViewModel.Styles.Image;
        model.PlaybackSpeed.RawText = "invalid";
        model.AnimationEnabled = false;
        model.Fit = (int)ImageFit.STRETCH;
        model.RefreshLanguage();
        Assert.Equal((int)ImageFit.STRETCH, model.Fit);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Null(context.Session.SelectedLayer!.Image!.Playback);
        Assert.Equal(ImageFit.STRETCH, context.Session.SelectedLayer.Image.Fit);
    }

    [Fact]
    public async Task ShapeParametersValidateThenConvertAndResizeTheActualPath()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 50) };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var model = context.Session.ViewModel.Styles.Shape;
        model.ShapeType = 4;
        model.ArrowHead.RawText = "100";
        model.ArrowShaft.RawText = "40";
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("ShapeArrowHeadInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(model.RestoreField("ShapeArrowHeadInput"));
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(ShapeKind.ARROW, context.Session.SelectedLayer!.Shape!.Kind);
        Assert.Equal(0.4, context.Session.SelectedLayer.Shape.ArrowShaftRatio);
        await context.Session.Graphics.ConvertSelectedShapeToPathAsync();
        Assert.Null(context.Session.LastError);
        var converted = context.Editor.Snapshot;
        var oldPath = context.Session.SelectedLayer!.Shape!.Path!;
        model.Width.RawText = "200";
        model.Height.RawText = "150";
        Assert.True(context.Session.TryCommitDrafts(false));
        var changed = context.Session.SelectedLayer.Shape!.Path!;
        Assert.Equal(new ScenePoint(oldPath.Start.X * 2, oldPath.Start.Y * 3), changed.Start);
        Assert.Equal(new ScenePoint(oldPath.Segments[0].Control1.X * 2, oldPath.Segments[0].Control1.Y * 3), changed.Segments[0].Control1);
        Assert.True(context.Editor.Undo());
        Assert.Same(converted, context.Editor.Snapshot);
    }

    private static ProjectDocument ImageDocument()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/test.png");
        return new() { Assets = [asset], Layers = [new() { Kind = LayerKind.IMAGE, Image = new(asset.Id, 200, 100) }] };
    }
}
