using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>以独立 libass 和手写静态偏移基线验证阴影钳制的导入导出语义。</summary>
public sealed class AssShadowClampReferenceTests
{
    private static readonly long[] sampleMilliseconds = [0, 500, 1000, 1500, 2000];

    [LibassReferenceTheory]
    [InlineData(-10, 10)]
    [InlineData(10, -10)]
    public void SignedShadInterpolationMatchesIndependentStaticOffsets(double initial, double target)
    {
        var source = ShadowScript(FormattableString.Invariant(
            $@"\xshad{initial}\yshad{initial}\t(0,2000,\shad{target})"));

        AssertOffsets(source, sampleMilliseconds, milliseconds =>
        {
            var fraction = milliseconds / 2000d;
            var value = Math.Max(initial + (target - initial) * fraction, 0);
            return new(value, value);
        });
    }

    [LibassReferenceTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceOrderClampsOnlyTheShadOperationBeforeTheNextAxisOperation(bool shadFirst)
    {
        var transforms = shadFirst
            ? @"\t(0,2000,\shad10)\t(0,2000,\xshad-6)"
            : @"\t(0,2000,\xshad-6)\t(0,2000,\shad10)";
        var source = ShadowScript(@"\xshad-10\yshad-10" + transforms);

        AssertOffsets(source, sampleMilliseconds, milliseconds =>
        {
            var fraction = milliseconds / 2000d;
            var clamped = Math.Max(-10 + 20 * fraction, 0);
            var x = shadFirst
                ? clamped * (1 - fraction) - 6 * fraction
                : Math.Max((-10 + 4 * fraction) * (1 - fraction) + 10 * fraction, 0);
            return new(x, clamped);
        });
    }

    [LibassReferenceFact]
    public void InstantShadClampsBeforeItsStartAndChangesAtTheExactBoundary()
    {
        var source = ShadowScript(@"\xshad-10\yshad-10\t(1000,1000,\shad10)");

        AssertOffsets(source, [0, 999, 1000, 1001, 2000], milliseconds =>
            milliseconds < 1000 ? new(0, 0) : new(10, 10));
    }

    private static void AssertOffsets(string source, long[] milliseconds, Func<long, ScenePoint> offsets)
    {
        var document = ReferenceSubtitleProject.Import(source);
        ProjectValidator.Validate(document);
        var normalized = AssSubtitleFormat.Write(document).Text;
        using var reference = new LibassReferenceRenderer();
        foreach (var time in milliseconds)
        {
            var offset = offsets(time);
            var expectedSource = ShadowScript(FormattableString.Invariant($@"\xshad{offset.X}\yshad{offset.Y}"));
            var expected = reference.Render(expectedSource, time);
            AssertShadow(expected, reference.Render(source, time));
            AssertShadow(expected, reference.Render(normalized, time));
        }
    }

    private static void AssertShadow(SubtitleReferenceFrame expected, SubtitleReferenceFrame actual)
    {
        var expectedEnergy = expected.Energy(0);
        var actualEnergy = actual.Energy(0);
        if (expectedEnergy == 0)
        {
            Assert.Equal(0, actualEnergy);
            return;
        }
        Assert.True(expectedEnergy > 1 && actualEnergy > 1);
        Assert.InRange(Math.Abs(actualEnergy / expectedEnergy - 1), 0, 0.02);
        var expectedBounds = ShadowBounds(expected);
        var actualBounds = ShadowBounds(actual);
        Assert.InRange(Math.Abs(expectedBounds.Left - actualBounds.Left), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Top - actualBounds.Top), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Right - actualBounds.Right), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Bottom - actualBounds.Bottom), 0, 1);
    }

    private static (int Left, int Top, int Right, int Bottom) ShadowBounds(SubtitleReferenceFrame frame)
    {
        var left = frame.Width;
        var top = frame.Height;
        var right = -1;
        var bottom = -1;
        for (var index = 0; index < frame.Width * frame.Height; index++)
        {
            if (frame.Pixels[index * 4] <= 0.01f)
            {
                continue;
            }
            left = Math.Min(left, index % frame.Width);
            top = Math.Min(top, index / frame.Width);
            right = Math.Max(right, index % frame.Width);
            bottom = Math.Max(bottom, index / frame.Width);
        }
        return (left, top, right, bottom);
    }

    private static string ShadowScript(string tags) =>
        ReferenceSubtitleProject.Script(@"{\1c&H00FF00&\4c&H0000FF&\4a&H00&\bord0" + tags + "}MMMM");
}
