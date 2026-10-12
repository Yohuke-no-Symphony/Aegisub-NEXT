using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssDormantKaraokeAnimationLossTests
{
    [Theory]
    [InlineData(SubtitleAnimationState.ACTIVE)]
    [InlineData(SubtitleAnimationState.INACTIVE)]
    public void StateFillWithoutKaraokeReportsItsOmittedAnimationWithoutCreatingTiming(SubtitleAnimationState state)
    {
        var line = Line();
        var track = Fill(state);
        var document = Document(line, track);

        var written = AssSubtitleFormat.Write(document);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeAnimation" &&
            diagnostic.SubtitleId == line.Id);
        Assert.Empty(imported.Karaoke);
        Assert.DoesNotContain("\\t(", written.Text, StringComparison.Ordinal);
        Assert.Equal(SceneColor.White, imported.Style.Fill);
        Assert.Same(track, Assert.Single(document.Layers[0].Tracks));
    }

    [Fact]
    public void StateFillRangeOutsideAllTimedTextReportsItsOmittedAnimation()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = Line() with
        {
            AnimationRanges = [range],
            Karaoke = [new(0, 1, new(1), new(2), SceneColor.White)]
        };
        var track = Fill(SubtitleAnimationState.INACTIVE, range.Id);
        var document = Document(line, track);

        var written = AssSubtitleFormat.Write(document);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Lines);

        Assert.Single(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeAnimation" &&
            diagnostic.SubtitleId == line.Id);
        Assert.Single(imported.Karaoke);
        Assert.DoesNotContain("\\t(", written.Text, StringComparison.Ordinal);
        Assert.Same(track, Assert.Single(document.Layers[0].Tracks));
        Assert.Same(range, Assert.Single(line.AnimationRanges));
    }

    [Fact]
    public void ConvertibleActiveAndInactiveFillAnimationsDoNotReportDormantLoss()
    {
        var line = Line() with { Karaoke = [new(0, 2, new(1), new(2), SceneColor.White)] };
        var active = Fill(SubtitleAnimationState.ACTIVE);
        var inactive = Fill(SubtitleAnimationState.INACTIVE);
        var document = Document(line, active, inactive);

        var written = AssSubtitleFormat.Write(document);

        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.DormantKaraokeAnimation");
        Assert.Contains("\\t(0,2000,1,\\1c", written.Text, StringComparison.Ordinal);
        Assert.Contains("\\t(0,2000,1,\\2c", written.Text, StringComparison.Ordinal);
        Assert.Same(active, document.Layers[0].Tracks[0]);
        Assert.Same(inactive, document.Layers[0].Tracks[1]);
    }

    private static AnimationTrack Fill(SubtitleAnimationState state, Guid? rangeId = null)
    {
        return new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: rangeId, State: state), [])
        {
            ColorSpace = AnimationColorSpace.SRGB,
            InitialValue = SceneColor.Black,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(2), new SceneColor(1, 0, 0))]
        };
    }

    private static SubtitleLine Line()
    {
        return new()
        {
            Text = "ab",
            End = new(2),
            Style = new()
            {
                FontFamily = "Noto Sans",
                FontSize = 20,
                StrokeWidth = 0,
                ShadowOffset = new(0, 0),
                ShadowColor = SceneColor.Transparent,
                ShadowBlur = 0,
                WrapMode = SubtitleWrapMode.NATURAL
            }
        };
    }

    private static ProjectDocument Document(SubtitleLine line, params AnimationTrack[] tracks)
    {
        var document = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End, Tracks = [.. tracks] }]
        };
        ProjectValidator.Validate(document);
        return document;
    }
}
