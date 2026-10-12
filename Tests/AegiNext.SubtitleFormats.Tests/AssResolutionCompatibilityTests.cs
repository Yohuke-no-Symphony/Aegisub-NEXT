using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssResolutionCompatibilityTests
{
    [Theory]
    [InlineData("yes", 2)]
    [InlineData("no", 1)]
    public void StyleInlineResetAndActiveVisualUseTheSameBorderAndShadowResolution(string scaled, double factor)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\bord6\\xshad8\\yshad12\\k50}a" +
            "{\\rAlternate\\bord\\xshad\\yshad\\k100\\t(500,500,\\bord10\\xshad14\\yshad16)}b", scaled, 1280, 720), 1280, 720);
        var line = Assert.Single(result.Lines);
        var first = OrdinaryAt(line, 0);
        var second = OrdinaryAt(line, 1);
        var active = line.KaraokeStyleSpans[^1].ActiveStyle!;

        Assert.Equal(2 * factor, line.Style.StrokeWidth);
        Assert.Equal(new ScenePoint(2 * factor, 2 * factor), line.Style.ShadowOffset);
        Assert.Equal(6 * factor, first.StrokeWidth);
        Assert.Equal(new ScenePoint(8 * factor, 12 * factor), first.ShadowOffset);
        Assert.Equal(4 * factor, second.StrokeWidth);
        Assert.Equal(new ScenePoint(3 * factor, 3 * factor), second.ShadowOffset);
        Assert.Equal(10 * factor, active.StrokeWidth);
        Assert.Equal(new ScenePoint(14 * factor, 16 * factor), active.ShadowOffset);
        Assert.Equal(40, line.Style.FontSize);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderLayoutResolution");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1280, null)]
    [InlineData(0, 720)]
    [InlineData(1280, -1)]
    public void UnscaledBorderWithoutCompleteLayoutResUsesTargetPixelsAndReportsTheAssumption(int? width, int? height)
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\pos(20,30)\\blur4\\k100}ab", "no", width, height), 1280, 720);
        var line = Assert.Single(result.Lines);

        Assert.Equal(2, line.Style.StrokeWidth);
        Assert.Equal(new ScenePoint(2, 2), line.Style.ShadowOffset);
        Assert.Equal(40, line.Style.FontSize);
        Assert.Equal(new ScenePoint(40, 60), line.Style.Position!.Offset);
        Assert.Equal(new SubtitleMargins(20, 20, 20), line.Style.Margins);
        Assert.Equal(8 * AssBlurConversion.SigmaPerUnit, OrdinaryAt(line, 0).StrokeBlur, 12);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderLayoutResolution");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurLayoutResolution");
        Assert.Single(line.Karaoke);
        Assert.Single(line.KaraokeStyleSpans);
    }

    [Fact]
    public void NonuniformLayoutResKeepsSeparateShadowAxesAndDiagnosesScalarBorderApproximation()
    {
        var result = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\pos(20,30)\\t(0,1000,\\bord6)}ab", "no", 1280, 1080), 640, 360);
        var clip = Assert.Single(result.Clips);
        var factor = Math.Sqrt(1d / 6);
        var border = Assert.Single(clip.Tracks, track => track.Property == AnimationProperty.STROKE_WIDTH);

        Assert.Equal(2 * factor, clip.Line.Style.StrokeWidth, 12);
        Assert.Equal(new ScenePoint(1, 2d / 3), clip.Line.Style.ShadowOffset);
        Assert.Equal(6 * factor, SceneEvaluator.EvaluateScalarTrack(border, new(1)), 12);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.BorderResampling");
        Assert.Equal(20, clip.Line.Style.FontSize);
        Assert.Equal(new ScenePoint(20, 30), clip.Line.Style.Position!.Offset);
    }

    [Fact]
    public void ResampledUnscaledAppearanceExportsAsNativeCanvasValuesWithoutSavingTheFlag()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File("{\\fscx200\\fscy200\\kt50\\k100\\t(500,500,\\bord10\\xshad12\\yshad8)}ab", "no", 1280, 720), 640, 360);
        var clip = Assert.Single(imported.Clips);
        var document = SubtitleFormatTestDocument.Create(clip.Line) with
        {
            Width = 640, Height = 360,
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = clip.Line.Id, Start = clip.Line.Start, End = clip.Line.End,
                Transform = clip.Transform, AnimationOffset = clip.ContentOffset, Tracks = clip.Tracks }]
        };
        var written = AssSubtitleFormat.Write(document);
        var reread = Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips);

        Assert.Contains("ScaledBorderAndShadow: yes", written.Text, StringComparison.Ordinal);
        Assert.Equal(0.5, clip.Line.Style.StrokeWidth);
        Assert.Equal(OrdinaryAt(clip.Line, 0).StrokeWidth, OrdinaryAt(reread.Line, 0).StrokeWidth, 9);
        Assert.Equal(new ScenePoint(0.5, 0.5), clip.Line.Style.ShadowOffset);
        Assert.Equal(OrdinaryAt(clip.Line, 0).ShadowOffset, OrdinaryAt(reread.Line, 0).ShadowOffset);
        Assert.Equal(2.5, clip.Line.KaraokeStyleSpans[0].ActiveStyle!.StrokeWidth);
        Assert.Equal(clip.Line.KaraokeStyleSpans[0].ActiveStyle!.StrokeWidth, reread.Line.KaraokeStyleSpans[0].ActiveStyle!.StrokeWidth);
    }

    private static SubtitleStyle OrdinaryAt(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(line.Style) ?? line.Style;
    }

    [Theory]
    [InlineData("{\\bord8194}a")]
    [InlineData("{\\xshad2000000002}a")]
    public void BorderResamplingOutsideNativeLimitsIsRejectedWithoutClamping(string text)
    {
        var source = AssBoundarySource.File(text, "no", 1280, 720);
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(source, 640, 360));
    }
}
