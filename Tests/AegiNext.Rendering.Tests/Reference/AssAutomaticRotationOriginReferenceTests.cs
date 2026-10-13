using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssAutomaticRotationOriginReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void FixedAutomaticPlacementKeepsWorldOriginAndMeasuredGlyphPlacement(int alignment)
    {
        var source = ReferenceSubtitleProject.Script($@"{{\q2\an{alignment}\4a&HFF&\org(205,155)\fscx125\fscy80\frz17}}Fj")
            .Replace(",7,32,32,32,1", ",7,100,100,100,1", StringComparison.Ordinal);
        using var measurer = new ReferenceFontPlacementMeasurer();
        var imported = AssSubtitleFormat.Parse(source, 640, 360, placementMeasurer: measurer);
        Assert.DoesNotContain(imported.Diagnostics, item => item.Code == "Ass.RotationOrigin");
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, imported, "ASS");
        var line = Assert.Single(document.Subtitles);
        Assert.Null(line.Style.Position);
        document = document with
        {
            Assets = [measurer.Font],
            Subtitles = [line with { Style = line.Style with { FontAssetId = measurer.Font.Id } }]
        };
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(document));
        var written = AssSubtitleFormat.Write(restored, placementMeasurer: measurer);
        Assert.DoesNotContain(written.Diagnostics, item => item.Code == "Ass.RotationOrigin");
        Assert.Contains(@"\org(205,155)", written.Text, StringComparison.Ordinal);
        var metrics = measurer.Measure(restored, restored.Subtitles[0]);
        var transform = Assert.Single(restored.Layers).Transform;
        Assert.Equal(new ScenePoint(205, 155), new(metrics.BasePosition.X + transform.Position.X,
            metrics.BasePosition.Y + transform.Position.Y));
        var anchor = new ScenePoint((alignment - 1) % 3 * 0.5, alignment <= 3 ? 1 : alignment <= 6 ? 0.5 : 0);
        var placement = new ScenePoint(
            metrics.BasePosition.X + transform.Scale.X * (metrics.BoundsOrigin.X + anchor.X * metrics.BoundsSize.X - metrics.Pivot.X),
            metrics.BasePosition.Y + transform.Scale.Y * (metrics.BoundsOrigin.Y + anchor.Y * metrics.BoundsSize.Y - metrics.Pivot.Y));
        var baseline = source.Replace(@"\org(", FormattableString.Invariant($@"\pos({placement.X},{placement.Y})\org("), StringComparison.Ordinal);
        using var reference = new LibassReferenceRenderer();
        var expected = reference.Render(baseline, 1000);
        var actual = reference.Render(written.Text, 1000);
        var energy = expected.Energy(3);
        Assert.True(energy > 100 && actual.Energy(3) > 100);
        Assert.InRange(Math.Abs(actual.Energy(3) / energy - 1), 0, 0.02);
        var difference = 0d;
        for (var index = 3; index < expected.Pixels.Length; index += 4)
        {
            difference += Math.Abs(actual.Pixels[index] - expected.Pixels[index]);
        }
        Assert.True(difference / energy <= 0.02, $"Difference {difference / energy}, expected {expected.InkBounds()}, actual {actual.InkBounds()}\n{written.Text}");
    }
}
