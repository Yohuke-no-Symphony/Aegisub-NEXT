using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class StyleBatchPresetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyingPresetToSelectedSubtitlesClearsTheirInlineFormattingWithOneUndo(bool applicationFont)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SetProjectLocation(null, context.DirectoryPath);
        var captured = await CreatePresetAsync(applicationFont);
        var preset = captured.Preset;
        if (applicationFont)
        {
            await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(captured));
            session.Styles.Refresh(preset.Id);
        }
        else
        {
            await session.Styles.UpsertAsync(preset);
        }
        var selected = document.Layers.Where(layer => layer.SubtitleId == document.Subtitles[0].Id ||
            layer.SubtitleId == document.Subtitles[1].Id).Select(layer => layer.Id).ToArray();
        session.SelectLayer(selected[0], selected);
        Assert.True(session.ViewModel.Styles.Subtitle.CanApplyPreset);

        await session.ApplySelectedStyleAsync();

        Assert.Null(session.LastError);
        Assert.False(session.IsProjectBusy);
        var changed = context.Editor.Snapshot;
        foreach (var original in document.Subtitles.Take(2))
        {
            var line = changed.Subtitles.Single(value => value.Id == original.Id);
            Assert.Equal(original with
            {
                Style = preset.Style, StyleName = preset.Name, StylePresetId = preset.Id, InlineSpans = []
            }, line);
        }
        Assert.Same(document.Subtitles[2], changed.Subtitles[2]);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.Equal(document.Assets, changed.Assets);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(LayerKind.SUBTITLE)]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task MixedSelectionCannotApplyPresetThroughPanelOrLibrary(LayerKind primaryKind)
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var preset = (await CreatePresetAsync(false)).Preset;
        await session.Styles.UpsertAsync(preset);
        var primary = document.Layers.First(layer => layer.Kind == primaryKind);
        session.SelectLayer(primary.Id, document.Layers.Select(layer => layer.Id).ToArray());

        Assert.False(session.ViewModel.Styles.Subtitle.CanApplyPreset);
        Assert.Empty(session.StyleSubtitleIds());
        await session.ApplySelectedStyleAsync();
        await session.Styles.ApplyAsync(preset);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Null(session.LastError);
    }

    [Fact]
    public async Task NonSubtitleSelectionCannotApplyPresetToThePreviouslySelectedSubtitle()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var preset = (await CreatePresetAsync(false)).Preset;
        await session.Styles.UpsertAsync(preset);
        session.SelectCue(document.Subtitles[0].Id);
        var shape = document.Layers.Single(layer => layer.Kind == LayerKind.SHAPE);
        session.SelectLayer(shape.Id, [shape.Id]);

        Assert.False(session.ViewModel.Styles.Subtitle.CanApplyPreset);
        await session.ApplySelectedStyleAsync();
        await session.Styles.ApplyAsync(preset);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static async Task<CapturedSubtitleStyle> CreatePresetAsync(bool applicationFont)
    {
        ImmutableArray<FontFamilyBundle> families = [];
        if (applicationFont)
        {
            var bytes = (await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"))).ToImmutableArray();
            var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
            families = [new("Noto Sans", [font])];
        }
        return new(new(Guid.NewGuid(), "Batch preset", new()
        {
            FontFamily = applicationFont ? "Noto Sans" : "sans-serif", FontSize = 96, Fill = new(0.2, 0.4, 0.6)
        }), families);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "First", End = new(4), Style = new() { FontFamily = "sans-serif", FontSize = 40 },
            InlineSpans = [new(0, 5, new() { Bold = true })],
            Karaoke = [new(0, 5, MediaTime.Zero, new(2), SceneColor.White)]
        };
        var second = new SubtitleLine
        {
            Text = "Second", Start = new(5), End = new(9), Style = new() { FontFamily = "sans-serif", FontSize = 72 },
            InlineSpans = [new(0, 6, new() { Italic = true })]
        };
        var third = new SubtitleLine { Text = "Unselected", Start = new(10), End = new(14) };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/fixture.png");
        var imageTrack = new ProjectTrack { Name = "Images" };
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        return new()
        {
            Tracks = [ProjectTrack.Default, imageTrack, shapeTrack],
            Assets = [asset], Subtitles = [first, second, third],
            Layers =
            [
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = third.Id, Start = third.Start, End = third.End },
                new() { Kind = LayerKind.IMAGE, TrackId = imageTrack.Id, Image = new(asset.Id, 120, 80), End = new(4) },
                new() { Kind = LayerKind.SHAPE, TrackId = shapeTrack.Id, Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(4) }
            ]
        };
    }
}
