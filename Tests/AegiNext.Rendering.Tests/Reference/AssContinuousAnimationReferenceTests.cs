using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssContinuousAnimationReferenceTests
{
    [LibassReferenceFact]
    public void ContinuousRgbAndChannelAlphaAgreeWithIndependentLibassAtInteriorTimes()
    {
        var source = ReferenceSubtitleProject.Script(@"{\4a&HFF&\1c&H000000&\t(\1c&HFFFFFF&\1a&H80&)}MMMM");
        var document = ReferenceSubtitleProject.Import(source);
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        var exported = AssSubtitleFormat.Write(document);
        foreach (var time in new long[] { 0, 1000, 2000, 3000, 3999 })
        {
            var expected = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(renderer, document, time);
            var copied = reference.Render(exported.Text, time);
            Assert.InRange(Math.Abs(actual.MaximumAlpha - expected.MaximumAlpha), 0, 0.005);
            Assert.InRange(Math.Abs(copied.MaximumAlpha - expected.MaximumAlpha), 0, 0.005);
            Assert.InRange(Math.Abs(Encode(StraightRed(actual)) - StraightRed(expected)), 0, 1d / 255 + 0.001);
            Assert.InRange(Math.Abs(StraightRed(copied) - StraightRed(expected)), 0, 1d / 255 + 0.001);
        }
    }

    [LibassReferenceTheory]
    [InlineData("\\fs96")]
    [InlineData("\\fs+10")]
    public void ContinuousAbsoluteAndRelativeFontSizeFollowIndependentLibassInkGrowth(string target)
    {
        var source = ReferenceSubtitleProject.Script("{\\4a&HFF&\\t(" + target + ")}MMMM");
        var document = ReferenceSubtitleProject.Import(source);
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        var referenceBase = reference.Render(source, 0).InkBounds();
        var nativeBase = ReferenceSubtitleProject.Render(renderer, document, 0).InkBounds();
        var widthScale = (double)(referenceBase.Right - referenceBase.Left) / (nativeBase.Right - nativeBase.Left);
        var heightScale = (double)(referenceBase.Bottom - referenceBase.Top) / (nativeBase.Bottom - nativeBase.Top);
        foreach (var time in new long[] { 0, 1000, 2000, 3000 })
        {
            var expected = reference.Render(source, time).InkBounds();
            var actual = ReferenceSubtitleProject.Render(renderer, document, time).InkBounds();
            Assert.InRange(Math.Abs((actual.Right - actual.Left) * widthScale - (expected.Right - expected.Left)), 0, 3);
            Assert.InRange(Math.Abs((actual.Bottom - actual.Top) * heightScale - (expected.Bottom - expected.Top)), 0, 3);
        }
    }

    private static double StraightRed(SubtitleReferenceFrame frame)
    {
        var sample = 0;
        for (var index = 4; index < frame.Pixels.Length; index += 4)
        {
            if (frame.Pixels[index + 3] > frame.Pixels[sample + 3])
            {
                sample = index;
            }
        }
        return frame.Pixels[sample + 3] > 0 ? frame.Pixels[sample] / frame.Pixels[sample + 3] : 0;
    }

    private static double Encode(double value) => value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1d / 2.4) - 0.055;
}
