using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests;

public sealed class AnimatedSubtitlePlacementTests
{
    [Fact]
    public void RangeFontSizeAnimationInvalidatesPlacementAndUsesReferenceScale()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 3);
        var line = new SubtitleLine { Text = "ABC", End = new(2), AnimationRanges = [range], Style = new()
        {
            FontFamily = "sans-serif", FontSize = 24,
            Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 }
        } };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Tracks = [new(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), [new(new(0), 24), new(new(2), 48)])]
        };
        var document = new ProjectDocument { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] };
        using var resolver = new LayerPlacementResolver();

        var first = resolver.Resolve(document, Path.GetTempPath(), layer, new(0));
        var end = resolver.Resolve(document, Path.GetTempPath(), layer, new(2));
        var held = resolver.Resolve(document, Path.GetTempPath(), layer, new(3));

        Assert.Null(end.Error);
        Assert.True(end.Geometry!.GlyphSize.X > first.Geometry!.GlyphSize.X * 1.8);
        Assert.Equal(2, end.Geometry.OffsetScale);
        Assert.Same(end, held);
    }

    [Fact]
    public void PositionMeasurementUsesAnimatedSpacingAtTheContentEndpointAndCachesOnlyItsEffectiveValue()
    {
        var line = new SubtitleLine
        {
            Text = "ABC DEF", End = new(2), Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontFamily = "sans-serif", FontSize = 24
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(2), 12)])]
        };
        var document = new ProjectDocument { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] };
        using var resolver = new LayerPlacementResolver();

        var first = resolver.Resolve(document, Path.GetTempPath(), layer, new(0));
        var end = resolver.Resolve(document, Path.GetTempPath(), layer, new(2));
        var held = resolver.Resolve(document, Path.GetTempPath(), layer, new(3));
        var staticResult = resolver.Resolve(document, Path.GetTempPath(), layer);

        Assert.Null(first.Error);
        Assert.Null(end.Error);
        Assert.True(end.Geometry!.GlyphSize.X > first.Geometry!.GlyphSize.X);
        Assert.Same(end, held);
        Assert.Equal(first.Geometry.GlyphSize, staticResult.Geometry!.GlyphSize);
        Assert.Equal(0, line.Style.LetterSpacing);
    }
}
