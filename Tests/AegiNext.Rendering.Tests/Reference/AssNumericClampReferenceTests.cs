using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>以独立 libass 和手写静态 ASS 帧验证数值钳制及非正字号的样式重置。</summary>
public sealed class AssNumericClampReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonPositiveAnimatedFontSizeResetsToTheStyleSizeAtTheExactBoundary(bool relative)
    {
        var source = Script(relative ? @"\fs96\t(0,2000,\fs-20)" : @"\fs96\t(0,2000,\fs0)");

        AssertFrames(source, [0, 500, 999, 1000, 1001, 1500, 1999, 2000], milliseconds =>
        {
            var fraction = milliseconds / 2000d;
            var raw = relative ? 96 * (1 - 2 * fraction) : 96 * (1 - fraction);
            var size = raw <= 0 ? 48 : raw;
            return FormattableString.Invariant($@"\fs{size}");
        });
    }

    [LibassReferenceTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothScaleAxesClampStaticNegativeValuesAndInterpolatedNegativeTargets(bool negativeInitial)
    {
        var source = Script(negativeInitial
            ? @"\fscx-200\fscy-100\t(0,2000,\fscx200\fscy100)"
            : @"\fscx200\fscy100\t(0,2000,\fscx-200\fscy-100)");

        AssertFrames(source, [0, 500, 999, 1000, 1001, 1500, 2000], milliseconds =>
        {
            var fraction = milliseconds / 2000d;
            var factor = negativeInitial ? fraction : Math.Max(1 - 2 * fraction, 0);
            return FormattableString.Invariant($@"\fscx{200 * factor}\fscy{100 * factor}");
        });
    }

    [LibassReferenceFact]
    public void BorderClampsEachOperationAfterAStaticNegativeReset()
    {
        var source = Script(@"\bord-6\t(0,1000,\bord6)\t(1000,2000,\bord-6)");

        AssertFrames(source, [0, 500, 999, 1000, 1250, 1499, 1500, 1501, 2000], milliseconds =>
        {
            var firstProgress = Math.Clamp(milliseconds / 1000d, 0, 1);
            var secondProgress = Math.Clamp((milliseconds - 1000) / 1000d, 0, 1);
            var width = Math.Max(6 * firstProgress * (1 - secondProgress) - 6 * secondProgress, 0);
            return FormattableString.Invariant($@"\bord{width}");
        });
    }

    [LibassReferenceFact]
    public void BlurClampsItsUpperAndLowerBoundsInSourceOperationOrder()
    {
        var source = Script(@"\blur60\t(0,2000,\blur200)\t(0,2000,\blur-20)");

        AssertFrames(source, [0, 500, 1000, 1500, 1800, 2000], milliseconds =>
        {
            var fraction = milliseconds / 2000d;
            var first = Math.Clamp(60 + 140 * fraction, 0, 100);
            var blur = Math.Clamp(first * (1 - fraction) - 20 * fraction, 0, 100);
            return FormattableString.Invariant($@"\blur{blur}");
        });
    }

    private static void AssertFrames(string source, long[] milliseconds, Func<long, string> staticTags)
    {
        var document = ReferenceSubtitleProject.Import(source);
        ProjectValidator.Validate(document);
        var normalized = AssSubtitleFormat.Write(document).Text;
        using var reference = new LibassReferenceRenderer();
        foreach (var time in milliseconds)
        {
            var expected = reference.Render(Script(staticTags(time)), time);
            AssertFrame(expected, reference.Render(source, time));
            AssertFrame(expected, reference.Render(normalized, time));
        }
    }

    private static void AssertFrame(SubtitleReferenceFrame expected, SubtitleReferenceFrame actual)
    {
        var expectedEnergy = expected.Energy(3);
        var actualEnergy = actual.Energy(3);
        if (expectedEnergy == 0)
        {
            Assert.Equal(0, actualEnergy);
            return;
        }
        Assert.True(actualEnergy > 0);
        Assert.InRange(Math.Abs(actualEnergy / expectedEnergy - 1), 0, 0.02);
        Assert.InRange(Math.Abs(actual.MaximumAlpha - expected.MaximumAlpha), 0, 0.005);
        var expectedBounds = expected.InkBounds();
        var actualBounds = actual.InkBounds();
        Assert.InRange(Math.Abs(expectedBounds.Left - actualBounds.Left), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Top - actualBounds.Top), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Right - actualBounds.Right), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Bottom - actualBounds.Bottom), 0, 1);
    }

    private static string Script(string tags) => ReferenceSubtitleProject.Script(@"{\4a&HFF&" + tags + "}MMMM");
}
