using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>使用独立 libass 验证矢量裁切的非闭合移动与连续样条在 ASS 交换后的覆盖。</summary>
public sealed class AssVectorMoveReferenceTests
{
    /// <summary>组合非闭合移动、样条及直线或贝塞尔路径与裁切模式、绘图比例。</summary>
    public static TheoryData<string, string, int> DrawingCases()
    {
        var cases = new TheoryData<string, string, int>();
        var drawings = new[]
        {
            "m 12 12 l 204 12 204 150 12 150 n 360 80 s 400 12 560 80 400 150 c",
            "m 12 12 l 204 12 n 400 80 l 204 150 12 150",
            "m 12 12 l 204 12 n 400 80 b 204 80 204 150 12 150",
            "m 12 80 s 12 12 204 12 204 80 s 204 150 12 150 12 80 c",
            "m 12 80 s 12 12 204 12 204 80 p 204 150 c s 12 150 12 80 12 12 c"
        };
        foreach (var drawing in drawings)
        {
            foreach (var tag in new[] { "clip", "iclip" })
            {
                foreach (var scale in new[] { 1, 2 })
                {
                    cases.Add(drawing, tag, scale);
                }
            }
        }
        return cases;
    }

    /// <summary>手写源帧和实际导入出口帧保持相同裁切覆盖、墨迹能量与边界。</summary>
    [LibassReferenceTheory]
    [MemberData(nameof(DrawingCases))]
    public void NonClosingMovesAndConsecutiveSplinesKeepIndependentLibassCoverage(string drawing, string tag, int scale)
    {
        var source = ReferenceSubtitleProject.Script(
            $@"{{\pos(0,0)\fs180\4a&HFF&\{tag}({scale},{drawing})}}MMMM");
        var document = ReferenceSubtitleProject.Import(source);
        ProjectValidator.Validate(document);
        var normalized = AssSubtitleFormat.Write(document).Text;
        using var reference = new LibassReferenceRenderer();
        var expected = reference.Render(source, 0);
        var actual = reference.Render(normalized, 0);
        var expectedEnergy = expected.Energy(3);
        var actualEnergy = actual.Energy(3);
        Assert.True(expectedEnergy > 100 && actualEnergy > 100);
        Assert.InRange(Math.Abs(actualEnergy / expectedEnergy - 1), 0, 0.02);
        Assert.InRange(Math.Abs(actual.MaximumAlpha - expected.MaximumAlpha), 0, 0.005);
        var coverageDifference = 0d;
        for (var index = 3; index < expected.Pixels.Length; index += 4)
        {
            coverageDifference += Math.Abs(actual.Pixels[index] - expected.Pixels[index]);
        }
        Assert.InRange(coverageDifference / expectedEnergy, 0, 0.02);
        var expectedBounds = expected.InkBounds();
        var actualBounds = actual.InkBounds();
        Assert.InRange(Math.Abs(expectedBounds.Left - actualBounds.Left), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Top - actualBounds.Top), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Right - actualBounds.Right), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Bottom - actualBounds.Bottom), 0, 1);
    }
}
