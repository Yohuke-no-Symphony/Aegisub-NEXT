using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>以手写 ASS 次要颜色通道验证原生普通填充和卡拉 OK 状态填充的优先级。</summary>
public sealed class AssKaraokeNormalFillReferenceTests
{
    private const double COLOR_TOLERANCE = 1d / 255 + 0.001;

    [LibassReferenceTheory]
    [InlineData("normal")]
    [InlineData("static")]
    [InlineData("animated")]
    public void NativeNormalFillAndInactiveOverridesAgreeWithIndependentSecondaryColorChannel(string inactiveMode)
    {
        var document = NativeDocument(inactiveMode);
        var source = Source(inactiveMode);
        var normalized = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 500, 999, 1000, 1500, 2000, 3999 })
        {
            var fraction = time / 4000d;
            var expected = time >= 1000 ? new SceneColor(0, 1, 0) : inactiveMode switch
            {
                "static" => new(0, 0, 1),
                "animated" => new(1, 1 - fraction, 0),
                _ => new(1 - fraction, 0, fraction)
            };
            var external = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(native, document, time);
            var copied = reference.Render(normalized, time);
            AssertColor(expected, external, false);
            AssertColor(expected, actual, true);
            AssertColor(expected, copied, false);
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.InRange(Math.Abs(Encode(Straight(actual, channel)) - Straight(external, channel)),
                    0, COLOR_TOLERANCE);
                Assert.InRange(Math.Abs(Straight(copied, channel) - Straight(external, channel)),
                    0, COLOR_TOLERANCE);
            }
        }
    }

    private static ProjectDocument NativeDocument(string inactiveMode)
    {
        var document = ReferenceSubtitleProject.Import(ReferenceSubtitleProject.Script("MMMM"));
        var original = Assert.Single(document.Subtitles);
        var line = original with
        {
            Style = original.Style with { Fill = SceneColor.Black, StrokeWidth = 0, ShadowColor = SceneColor.Transparent },
            InlineSpans = [],
            Karaoke = [new(0, 4, new(1), new(2), new(0, 1, 0)) { HighlightKind = KaraokeHighlightKind.STEP }],
            KaraokeStyleSpans = inactiveMode == "normal" ? [] :
                [new(0, 4, InactiveStyle: new() { Fill = new(0, 0, 1) })]
        };
        var normal = Fill(new(1, 0, 0), new(0, 0, 1), SubtitleAnimationState.NORMAL);
        var layer = Assert.Single(document.Layers) with
        {
            Tracks = inactiveMode == "animated"
                ? [normal, Fill(new(1, 1, 0), new(1, 0, 0), SubtitleAnimationState.INACTIVE)]
                : [normal]
        };
        var result = document with { Subtitles = [line], Layers = [layer] };
        ProjectValidator.Validate(result);
        return result;
    }

    private static AnimationTrack Fill(SceneColor initial, SceneColor target, SubtitleAnimationState state) =>
        new(new AnimationTrackTarget(AnimationProperty.FILL, State: state), [])
        {
            ColorSpace = AnimationColorSpace.SRGB,
            InitialValue = initial,
            Transforms = [new(Guid.NewGuid(), MediaTime.Zero, new(4), target)]
        };

    private static string Source(string inactiveMode)
    {
        var secondary = inactiveMode switch
        {
            "static" => @"\2c&HFF0000&",
            "animated" => @"\2c&H00FFFF&\t(0,4000,\2c&H0000FF&)",
            _ => @"\2c&H0000FF&\t(0,4000,\2c&HFF0000&)"
        };
        return ReferenceSubtitleProject.Script(@"{\4a&HFF&\1c&H00FF00&\1a&H00&\2a&H00&" +
            secondary + @"}{\k100}{\k100}MMMM");
    }

    private static void AssertColor(SceneColor expected, SubtitleReferenceFrame actual, bool linear)
    {
        Assert.True(actual.Energy(3) > 100);
        Assert.InRange(Math.Abs(actual.MaximumAlpha - 1), 0, 0.002);
        var expectedComponents = new[] { expected.Red, expected.Green, expected.Blue };
        for (var channel = 0; channel < 3; channel++)
        {
            var value = Straight(actual, channel);
            Assert.InRange(Math.Abs((linear ? Encode(value) : value) - expectedComponents[channel]),
                0, COLOR_TOLERANCE);
        }
    }

    private static double Straight(SubtitleReferenceFrame frame, int channel) => frame.Energy(channel) / frame.Energy(3);

    private static double Encode(double value) => value <= 0.0031308
        ? value * 12.92 : 1.055 * Math.Pow(value, 1d / 2.4) - 0.055;
}
