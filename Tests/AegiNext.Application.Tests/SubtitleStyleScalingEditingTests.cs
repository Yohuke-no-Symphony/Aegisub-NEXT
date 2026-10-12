using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleStyleScalingEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportIntoStyledTrackRebasesPreservedInlineAndHighlightValues(bool useTrackDefault)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1) { Offset = new(7, 9) };
        var source = new SubtitleLine
        {
            Text = "AB", End = new(2), Style = new() { FontSize = 48 },
            InlineSpans = [new(1, 1, new() { FontSize = 72 })], AnimationRanges = [range],
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White)],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Highlight", StrokeWidth = 3 },
            KaraokeStyleSpans = [new(0, 1, new() { ShadowOffset = new(5, -7) })]
        };
        var target = new SubtitleStyle { FontSize = 96, Scaling = new() { ReferenceWidth = 3840, ReferenceHeight = 2160 } };
        var document = new ProjectDocument();
        if (useTrackDefault)
        {
            document = ProjectEditingOperations.SetSubtitleTrackStyle(document, ProjectTrack.DEFAULT_TRACK_ID,
                Guid.NewGuid(), "Target", target);
        }

        var result = ProjectEditingOperations.CreateSubtitleClips(document, [source], ProjectTrack.DEFAULT_TRACK_ID,
            useTrackDefault ? null : target);
        var imported = Assert.Single(result.Subtitles);

        Assert.Equal(target, imported.Style);
        Assert.Equal(144, imported.InlineSpans[0].Style.FontSize);
        Assert.Equal(6, imported.KaraokeStyle!.StrokeWidth);
        Assert.Equal(new ScenePoint(10, -14), imported.KaraokeStyleSpans[0].ActiveStyle!.ShadowOffset);
        Assert.Same(source.Karaoke[0], imported.Karaoke[0]);
        Assert.Same(range, imported.AnimationRanges[0]);
        Assert.Equal(72, source.InlineSpans[0].Style.FontSize);
        Assert.Empty(result.Layers[0].Tracks);
    }

    [Fact]
    public void MergeConvertsSecondLineInlineAndKaraokeToFirstReferenceUnits()
    {
        var first = new SubtitleLine
        {
            Text = "ab", Start = new(0), End = new(2),
            Style = new() { FontSize = 40 },
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White)],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "First", StrokeWidth = 4, ShadowOffset = new(6, 8) }
        };
        var second = new SubtitleLine
        {
            Text = "cd", Start = new(2), End = new(4),
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 960, ReferenceHeight = 540 }, FontSize = 20
            },
            InlineSpans = [new(1, 1, new() { FontSize = 30, LetterSpacing = 3 })],
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White)],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Second", StrokeWidth = 2, ShadowOffset = new(3, 4) },
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 3 }, new() { ShadowOffset = new(5, 7) })]
        };
        var document = Document(first, second);
        var merged = ProjectEditingOperations.MergeSubtitles(document, first.Id, second.Id, string.Empty);
        var line = Assert.Single(merged.Subtitles);

        Assert.Equal("abcd", line.Text);
        Assert.Same(first.Style, line.Style);
        Assert.Same(first.KaraokeStyle, line.KaraokeStyle);
        Assert.Equal(40, OrdinaryAt(line, 2).FontSize);
        Assert.Equal(60, OrdinaryAt(line, 3).FontSize);
        Assert.Equal(6, OrdinaryAt(line, 3).LetterSpacing);
        Assert.Equal(6, KaraokeVisualStyleResolver.RangeStyleAt(line, 2, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        Assert.Equal(new ScenePoint(10, 14), KaraokeVisualStyleResolver.RangeStyleAt(line, 2, KaraokeVisualState.INACTIVE)!.ShadowOffset);
        Assert.Equal(first.Karaoke[0].Id, line.Karaoke[0].Id);
        Assert.Equal(second.Karaoke[0].Id, line.Karaoke[1].Id);
        Assert.Same(second, document.Subtitles[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyingWholeLineOrTrackRebasesRetainedKaraokeAndAppearanceAnimations(bool applyTrack)
    {
        using var directory = new TemporaryProjectDirectory();
        var rangeId = Guid.NewGuid();
        var line = new SubtitleLine
        {
            Text = "ab", Start = new(0), End = new(2),
            InlineSpans = [new(0, 1, new() { FontSize = 80 })],
            AnimationRanges = [new(rangeId, 0, 1) { Offset = new(5, 7) }],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Highlight", StrokeWidth = 3, ShadowBlur = 4 },
            KaraokeStyleSpans = [new(0, 1, new() { ShadowOffset = new(7, 9) }, new() { StrokeWidth = 5 })]
        };
        var font = new AnimationTrack(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 32,
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(1), 64),
                new(Guid.NewGuid(), new(1), new(2), 1.5) { Mode = AnimationTransformMode.MULTIPLY_BY }
            ]
        };
        var position = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.POSITION) { TextRangeId = rangeId },
            [new(new(0), new ScenePoint(11, 13)), new(new(2), new ScenePoint(21, 23))]);
        var blur = new AnimationTrack(AnimationProperty.BLUR, [new(new(0), 2), new(new(2), 4)]);
        var document = Document(line) with
        {
            Layers = [Layer(line) with { Tracks = [font, position, blur], Blur = 3 }]
        };
        var targetStyle = new SubtitleStyle
        {
            Scaling = new() { ReferenceWidth = 3840, ReferenceHeight = 2160 }, FontSize = 128
        };
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Scaled", targetStyle);
        var applied = applyTrack
            ? ProjectEditingOperations.SetSubtitleTrackStyle(document, ProjectTrack.DEFAULT_TRACK_ID, preset.Id, preset.Name, targetStyle, true)
            : await SubtitleStylePresetService.ApplyAsync(preset, document, directory.Path, [line.Id]);
        var result = Assert.Single(applied.Subtitles);
        var resultLayer = Assert.Single(applied.Layers);
        var convertedFont = Assert.Single(resultLayer.Tracks, track => track.Property == AnimationProperty.FONT_SIZE);

        Assert.Equal(targetStyle, result.Style);
        Assert.Empty(result.InlineSpans);
        Assert.Equal(6, result.KaraokeStyle!.StrokeWidth);
        Assert.Equal(8, result.KaraokeStyle.ShadowBlur);
        Assert.Equal(new ScenePoint(14, 18), result.KaraokeStyleSpans[0].ActiveStyle!.ShadowOffset);
        Assert.Equal(10, result.KaraokeStyleSpans[0].InactiveStyle!.StrokeWidth);
        Assert.Equal(64, convertedFont.InitialValue!.Value.Scalar);
        Assert.Equal(128, convertedFont.Transforms[0].Value.Scalar);
        Assert.Equal(1.5, convertedFont.Transforms[1].Value.Scalar);
        Assert.Same(position, Assert.Single(resultLayer.Tracks, track => track.Property == AnimationProperty.POSITION));
        Assert.Same(blur, Assert.Single(resultLayer.Tracks, track => track.Property == AnimationProperty.BLUR));
        Assert.Equal(3, resultLayer.Blur);
        Assert.Equal(line.AnimationRanges, result.AnimationRanges);
        Assert.Equal(document.Subtitles[0], line);
        Assert.Equal(32, font.InitialValue!.Value.Scalar);
    }

    private static SubtitleStyle OrdinaryAt(SubtitleLine line, int offset) => line.InlineSpans.FirstOrDefault(span =>
        span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;

    private static ProjectDocument Document(params SubtitleLine[] lines) => new()
    {
        Subtitles = lines.ToImmutableArray(), Layers = lines.Select(Layer).ToImmutableArray()
    };

    private static ProjectLayer Layer(SubtitleLine line) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
    };
}
