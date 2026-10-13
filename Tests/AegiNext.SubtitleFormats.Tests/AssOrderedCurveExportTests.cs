using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssOrderedCurveExportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void LinearMoveUsesTheOperationCurveInsteadOfItsLegacyAcceleration(double acceleration)
    {
        var track = new AnimationTrack(AnimationProperty.POSITION, [])
        {
            InitialValue = new ScenePoint(0, 0),
            Transforms = [new(Guid.NewGuid(), new(0), new(2), new ScenePoint(80, 40), acceleration)
            {
                Curve = new(KeyframeInterpolation.LINEAR)
            }]
        };

        var exported = AssSubtitleFormat.Write(Document(track));

        Assert.Contains("\\move(", exported.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code is "Ass.MoveApproximation" or "Subtitle.Composition");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void LinearOpacityCurveKeepsTheFullDurationRegardlessOfLegacyAcceleration(double acceleration)
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY, [])
        {
            InitialValue = 0,
            Transforms = [new(Guid.NewGuid(), new(0), new(2), 1, acceleration)
            {
                Curve = new(KeyframeInterpolation.LINEAR)
            }]
        };

        var exported = AssSubtitleFormat.Write(Document(track));

        Assert.Contains("\\fade(255,0,0,0,2000,2000,2000)", exported.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code is "Ass.OpacityApproximation" or "Subtitle.Composition");
    }

    [Theory]
    [InlineData(KeyframeInterpolation.LINEAR, 1, false)]
    [InlineData(KeyframeInterpolation.POWER, 2, false)]
    [InlineData(KeyframeInterpolation.EASE_OUT, 1, true)]
    public void RectangleMaskCurveUsesItsOwnExponentOrExpandsEvents(KeyframeInterpolation interpolation, int exponent, bool expanded)
    {
        var track = MaskTrack(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, new(0, 0), new(40, 20), interpolation);

        var exported = AssSubtitleFormat.Write(Document(track, mask: new RectangleClipMask { BottomRight = new(100, 100) }));

        Assert.Equal(expanded, exported.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded"));
        if (!expanded)
        {
            Assert.Contains("\\t(0,2000," + exponent + ",\\clip(40,20,100,100))", exported.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RectangleCornersWithDifferentOrderedCurvesUseEventExpansion()
    {
        var top = MaskTrack(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, new(0, 0), new(40, 20), KeyframeInterpolation.LINEAR);
        var bottom = MaskTrack(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT, new(100, 100), new(140, 120), KeyframeInterpolation.POWER);
        var document = Document(top, bottom, new RectangleClipMask { BottomRight = new(100, 100) });

        var exported = AssSubtitleFormat.Write(document);

        Assert.Contains(exported.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
    }

    private static AnimationTrack MaskTrack(AnimationProperty property, ScenePoint initial, ScenePoint target,
        KeyframeInterpolation interpolation) => new(property, [])
    {
        InitialValue = initial,
        Transforms = [new(Guid.NewGuid(), new(0), new(2), target, 7)
        {
            Curve = new(interpolation) { Exponent = 2 }
        }]
    };

    private static ProjectDocument Document(AnimationTrack track, AnimationTrack? second = null, ClipMask? mask = null)
    {
        var line = new SubtitleLine
        {
            Text = "Curve", End = new(2), Style = new()
            {
                Alignment = TextAlignment.TOP_LEFT, Position = new() { Offset = new(120, 80) },
                StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = 640, Height = 360, FrameRate = new(4, 1), Subtitles = [line],
            Layers = [new()
            {
                SubtitleId = line.Id, End = line.End, Mask = mask, Tracks = second is null ? [track] : [track, second]
            }]
        };
    }
}
