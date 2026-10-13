using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssMixedClipReferenceTests
{
    [LibassReferenceTheory]
    [InlineData("clip", "clip")]
    [InlineData("clip", "iclip")]
    [InlineData("iclip", "clip")]
    [InlineData("iclip", "iclip")]
    public void StaticMixedClipRegionsKeepIndependentLibassCoverage(string vectorTag, string rectangleTag)
    {
        var source = ReferenceSubtitleProject.Script($@"{{\pos(0,0)\fs180\4a&HFF&\{vectorTag}(m 20 10 l 300 10 300 200 20 200)\{rectangleTag}(120,0,450,220)}}MMMM");
        Compare(source);
    }

    [LibassReferenceTheory]
    [InlineData(@"\clip(m 20 10 l 300 10 300 200 20 200)\iclip(m invalid)\clip(120,0,450,220)")]
    [InlineData(@"\clip(m 20 10 l 300 10 300 200 20 200 m 70 70 l 70 130 230 130 230 70)\clip(50,0,450,220)")]
    [InlineData(@"\clip(m 20 10 l 300 10 300 200 20 200)\t(\clip(120,0,450,220))\clip(120,0,450,220)")]
    [InlineData(@"\clip(m 20 10 l 300 10 300 200 p 20 200 c)")]
    public void DuplicateVectorsHolesAndPermissiveSplineCommandsPreserveSourceRegions(string tags)
    {
        Compare(ReferenceSubtitleProject.Script(@"{\pos(0,0)\fs180\4a&HFF&" + tags + "}MMMM"));
    }

    private static void Compare(string source)
    {
        var imported = AssSubtitleFormat.Parse(source, 640, 360, maskGeometryCombiner: new AssMaskGeometryCombiner());
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, imported, "ASS");
        var normalized = AssSubtitleFormat.Write(document).Text;
        using var reference = new LibassReferenceRenderer();
        var expected = reference.Render(source, 500);
        var actual = reference.Render(normalized, 500);
        var energy = expected.Energy(3);
        Assert.True(energy > 100 && actual.Energy(3) > 100);
        Assert.InRange(Math.Abs(actual.Energy(3) / energy - 1), 0, 0.02);
        var difference = 0d;
        for (var index = 3; index < expected.Pixels.Length; index += 4)
        {
            difference += Math.Abs(actual.Pixels[index] - expected.Pixels[index]);
        }
        Assert.InRange(difference / energy, 0, 0.02);
        var first = expected.InkBounds();
        var second = actual.InkBounds();
        Assert.InRange(Math.Abs(first.Left - second.Left), 0, 1);
        Assert.InRange(Math.Abs(first.Top - second.Top), 0, 1);
        Assert.InRange(Math.Abs(first.Right - second.Right), 0, 1);
        Assert.InRange(Math.Abs(first.Bottom - second.Bottom), 0, 1);
    }
}
