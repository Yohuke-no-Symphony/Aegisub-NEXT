using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleAppearanceWorkflowTests
{
    [Theory]
    [InlineData(AnimationProperty.LETTER_SPACING)]
    [InlineData(AnimationProperty.FILL_BLUR)]
    [InlineData(AnimationProperty.STROKE_BLUR)]
    public async Task StyleInspectorEditsAndRestoresTheEvaluatedKeyframeWithoutOverwritingItsBase(AnimationProperty property)
    {
        var line = new SubtitleLine { Text = "Appearance", End = new(4), Style = new() { LetterSpacing = 1, FillBlur = 2, StrokeBlur = 3 } };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Tracks = [new(property, [new(new(0), 4), new(new(2), 8)])]
        };
        await using var context = new WorkspaceSessionTestContext(new() { Subtitles = [line], Layers = [layer] });
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, property, new(2), new(2))));
        var styles = context.Session.ViewModel.Styles.Subtitle;
        Assert.Equal("8", Read(styles, property));
        var original = context.Editor.Snapshot;
        Write(styles, property, "7e-");
        styles.ShadowXText = "12";
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.True(styles.RestoreNumberField(Label(property) + "Input"));
        Assert.Equal("8", Read(styles, property));
        Assert.Equal("12", styles.ShadowXText);
        styles.RestoreNumberField("ShadowXInput");
        Write(styles, property, "6");
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(6, context.Session.SelectedLayer!.Tracks.Single().Keyframes[^1].Value.Scalar);
        Assert.Equal(line.Style, context.Session.SelectedCue!.Style);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.Equal("8", Read(styles, property));
    }

    [Fact]
    public async Task ChangingBetweenSubtitleAndShapeRefreshesChoicesAndClearsAnInapplicableActiveProperty()
    {
        var line = new SubtitleLine { Text = "Appearance" };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var track = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer { TrackId = track.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 60), End = line.End };
        await using var context = new WorkspaceSessionTestContext(new()
        {
            Tracks = [ProjectTrack.Default, track], Subtitles = [line], Layers = [layer, shape]
        });
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var effects = context.Session.ViewModel.Effects;
        Assert.Contains(effects.Properties, choice => choice.Property == AnimationProperty.LETTER_SPACING);
        effects.Property = AnimationProperty.LETTER_SPACING;
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        Assert.DoesNotContain(effects.Properties, choice => AnimationPropertyMetadata.IsSubtitleOnlyProperty(choice.Property));
        Assert.Equal(AnimationProperty.OPACITY, effects.Property);
        context.Session.SelectCue(line.Id);
        Assert.Contains(effects.Properties, choice => choice.Property == AnimationProperty.FILL_BLUR);
        Assert.Contains(effects.Properties, choice => choice.Property == AnimationProperty.STROKE_BLUR);
    }

    private static string Label(AnimationProperty property) => property switch
    {
        AnimationProperty.LETTER_SPACING => "LetterSpacing",
        AnimationProperty.FILL_BLUR => "FillBlur",
        _ => "StrokeBlur"
    };

    private static string Read(SubtitleStyleViewModel model, AnimationProperty property) => property switch
    {
        AnimationProperty.LETTER_SPACING => model.LetterSpacingText,
        AnimationProperty.FILL_BLUR => model.FillBlurText,
        _ => model.StrokeBlurText
    };

    private static void Write(SubtitleStyleViewModel model, AnimationProperty property, string value)
    {
        switch (property)
        {
            case AnimationProperty.LETTER_SPACING:
                model.LetterSpacingText = value;
                break;
            case AnimationProperty.FILL_BLUR:
                model.FillBlurText = value;
                break;
            case AnimationProperty.STROKE_BLUR:
                model.StrokeBlurText = value;
                break;
        }
    }
}
