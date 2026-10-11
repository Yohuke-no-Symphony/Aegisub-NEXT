using AegiNext.Application;
using AegiNext.Application.Presets;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class GraphicEffectWorkflowTests
{
    [Theory]
    [InlineData(LayerKind.IMAGE, ShapeKind.RECTANGLE)]
    [InlineData(LayerKind.SHAPE, ShapeKind.RECTANGLE)]
    [InlineData(LayerKind.SHAPE, ShapeKind.PATH)]
    public async Task FadePresetAnimatesImagesShapesAndDrawingsWithOneUndo(LayerKind kind, ShapeKind shapeKind)
    {
        var document = Document(kind, shapeKind);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        await context.Session.EffectScripts.Completion;
        var session = context.Session;
        var layer = document.Layers[0];
        session.SelectLayer(layer.Id, [layer.Id]);
        var model = session.ViewModel.Effects;
        Assert.DoesNotContain(Localization.Get("Workbench.LetterBounce"), model.Presets);
        Assert.DoesNotContain(Localization.Get("Workbench.LetterPulse"), model.Presets);
        model.Preset = Array.IndexOf(model.Presets, Localization.Get("Workbench.Fade"));
        Assert.True(model.Preset >= 0);

        await session.ApplySelectedPresetAsync();

        var applied = context.Editor.Snapshot;
        Assert.Null(session.LastError);
        Assert.Equal(0, Assert.Single(SceneEvaluator.Evaluate(applied, new(0))).Opacity);
        Assert.Equal(0.6, Assert.Single(SceneEvaluator.Evaluate(applied, new(3, 20))).Opacity, 10);
        Assert.Equal(0.8, Assert.Single(SceneEvaluator.Evaluate(applied, new(2))).Opacity, 10);
        Assert.Equal(0.6, Assert.Single(SceneEvaluator.Evaluate(applied, new(77, 20))).Opacity, 10);
        Assert.Same(layer.Image, applied.Layers[0].Image);
        Assert.Same(layer.Shape, applied.Layers[0].Shape);
        Assert.Equal(layer.Transform, applied.Layers[0].Transform);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(applied));
        Assert.Equal(0.6, Assert.Single(SceneEvaluator.Evaluate(restored, new(77, 20))).Opacity, 10);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(applied, context.Editor.Snapshot);
    }

    [Fact]
    public async Task MixedSelectionAppliesEveryTargetAndTextOnlyFailureCannotPublishAPartialBatch()
    {
        var graphics = Document(LayerKind.SHAPE, ShapeKind.RECTANGLE);
        var subtitleTrack = new ProjectTrack { Name = "Subtitles" };
        var line = new SubtitleLine { Text = "AB", Start = new(0), End = new(4) };
        var subtitle = new ProjectLayer { TrackId = subtitleTrack.Id, SubtitleId = line.Id, Start = line.Start, End = line.End };
        var document = graphics with { Tracks = [ProjectTrack.Default, subtitleTrack], Subtitles = [line], Layers = [subtitle, graphics.Layers[0]] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        await context.Session.EffectScripts.Completion;
        var session = context.Session;
        session.SelectLayer(subtitle.Id, document.Layers.Select(layer => layer.Id).ToArray());
        Assert.DoesNotContain(Localization.Get("Workbench.LetterPulse"), session.ViewModel.Effects.Presets);

        await Assert.ThrowsAsync<EffectScriptException>(() => session.EffectScripts.ApplyAsync(BuiltinEffectScripts.Get("letter-pulse").Source));
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        await session.EffectScripts.ApplyAsync(BuiltinEffectScripts.Get("fade-in-out").Source);
        Assert.All(context.Editor.Snapshot.Layers, layer => Assert.Single(layer.Tracks));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        session.SelectLayer(subtitle.Id, [subtitle.Id]);
        Assert.Contains(Localization.Get("Workbench.LetterPulse"), session.ViewModel.Effects.Presets);
    }

    [Fact]
    public async Task PersonalPresetRequirementsRefreshOnEditsAndAllowWholeGraphicScopes()
    {
        var document = Document(LayerKind.SHAPE, ShapeKind.RECTANGLE);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        await context.Session.EffectScripts.Completion;
        var session = context.Session;
        session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id]);
        var preset = new EffectScriptPreset(Guid.NewGuid(), "Personal scope", ScopedSource("opacity"));
        await session.EffectScripts.UpsertAsync(preset);
        Assert.Contains(preset.Name, session.ViewModel.Effects.Presets);
        session.ViewModel.Effects.Preset = Array.IndexOf(session.ViewModel.Effects.Presets, preset.Name);
        await session.ApplySelectedPresetAsync();
        Assert.Single(context.Editor.Snapshot.Layers[0].Tracks);
        Assert.True(context.Editor.Undo());

        await session.EffectScripts.UpsertAsync(preset with { Source = ScopedSource("font-size") });
        Assert.DoesNotContain(preset.Name, session.ViewModel.Effects.Presets);
        await session.EffectScripts.UpsertAsync(preset);
        Assert.Contains(preset.Name, session.ViewModel.Effects.Presets);
    }

    private static string ScopedSource(string property) => $$"""
        effect "personal-scope" version 2
        short-clip compress
        scope clip current
            segment hold flex 1
                at 0 {{property}} base
                at 1 {{property}} base
            end
        end
        """;

    private static ProjectDocument Document(LayerKind kind, ShapeKind shapeKind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/test.png");
        var shape = shapeKind == ShapeKind.PATH
            ? new LayerShape(shapeKind, 100, 60)
            {
                Path = new(new(0, 0), [new(new(30, 0), new(60, 40), new(100, 60))])
            }
            : new LayerShape(shapeKind, 100, 60);
        return new()
        {
            Assets = kind == LayerKind.IMAGE ? [asset] : [],
            Layers = [new()
            {
                Kind = kind, End = new(4), Opacity = 0.8,
                Image = kind == LayerKind.IMAGE ? new(asset.Id, 100, 60) : null,
                Shape = kind == LayerKind.SHAPE ? shape : null,
                Transform = new() { X = 50, Y = 30, ScaleX = 1.5, ScaleY = 0.75, Rotation = 20 }
            }]
        };
    }
}
