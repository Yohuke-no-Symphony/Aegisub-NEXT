using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleAnimationWorkflowTests
{
    [Fact]
    public void RangeScriptPersistsAndRendersThroughTheSameProjectSnapshotUsedForAssExchange()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = new SubtitleLine
        {
            Text = "ABC", End = new(3), AnimationRanges = [range], Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontFamily = "Noto Sans", FontAssetId = font.Id, FontSize = 24, StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent, Alignment = TextAlignment.TOP_LEFT,
                Margins = new(16, 16, 16), WrapMode = SubtitleWrapMode.NO_WRAP
            }
        };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var document = new ProjectDocument { Width = 640, Height = 360, Assets = [font], Subtitles = [line], Layers = [layer] };
        var script = EffectScriptParser.Parse("""
            effect "range-typography" version 1
            short-clip compress
            segment animate flex 1
                at 0 font-size 24
                at 1 font-size 48
                at 0 shadow-offset (0, 0)
                at 1 shadow-offset (9, 6)
                at 0 fill rgba(0, 0, 0, 1)
                at 1 fill rgba(1, 1, 1, 0.5)
            end
            """);
        var prepared = ProjectEditingOperations.ApplyEffectScript(document, [layer.Id], script,
            new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id));
        var persisted = ProjectStore.Deserialize(ProjectStore.Serialize(prepared));
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var original = renderer.Render(prepared, new(1));
        using var restored = renderer.Render(persisted, new(1));

        Assert.Equal(original.CopySrgbBgra(), restored.CopySrgbBgra());
        var layout = renderer.MeasureSubtitleTextLayout(persisted, Assert.Single(SceneEvaluator.Evaluate(persisted, new(1))));
        Assert.Equal(32, layout.Runs.Single(run => run.Utf16Start == 1).Style.FontSize, 8);
        Assert.Equal(24, layout.Runs.First().Style.FontSize);
        var written = AssSubtitleFormat.Write(persisted);
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(written.Text, 640, 360), "ASS");
        var importedRange = Assert.Single(imported.Subtitles[0].AnimationRanges);
        var size = Assert.Single(imported.Layers[0].Tracks, track => track.Property == AnimationProperty.FONT_SIZE &&
            track.Target.TextRangeId == importedRange.Id);
        Assert.Equal(32, SceneEvaluator.EvaluateScalarTrack(size, new(1)), 6);
        Assert.Contains(imported.Layers[0].Tracks, track => track.Property == AnimationProperty.SHADOW_OFFSET);
        Assert.Contains(imported.Layers[0].Tracks, track => track.Property == AnimationProperty.FILL);
    }
}
