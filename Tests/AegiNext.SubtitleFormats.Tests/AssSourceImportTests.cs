using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssSourceImportTests
{
    [Theory]
    [InlineData(true, true, 640, 360)]
    [InlineData(true, false, 640, 1080)]
    [InlineData(false, true, 1920, 360)]
    [InlineData(false, false, 1920, 1080)]
    public void SourceResolutionPreservesDeclaredAxesAndFallsBackOnlyForMissingAxes(
        bool includeX, bool includeY, int expectedWidth, int expectedHeight)
    {
        var imported = AssSubtitleFormat.ParseAtSourceResolution(File(@"{\an7\pos(120,80)}text", includeX, includeY));
        var line = Assert.Single(imported.Result.Lines);

        Assert.Equal((expectedWidth, expectedHeight), (imported.Width, imported.Height));
        Assert.Equal(20, line.Style.FontSize);
        Assert.Equal(new ScenePoint(120, 80), line.Style.Position!.Offset);
        Assert.Equal((expectedWidth, expectedHeight),
            (line.Style.Scaling.ReferenceWidth, line.Style.Scaling.ReferenceHeight));
        if (includeX && includeY)
        {
            Assert.DoesNotContain(imported.Result.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
        }
        else
        {
            var diagnostic = Assert.Single(imported.Result.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
            if (!includeX)
            {
                Assert.Contains("PlayResX=1920", diagnostic.Message);
            }
            if (!includeY)
            {
                Assert.Contains("PlayResY=1080", diagnostic.Message);
            }
        }
    }

    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(true, false, 2, 1)]
    [InlineData(false, true, 1, 2)]
    public void TargetResolutionKeepsItsOwnMissingAxisFallbackAndScalingReference(
        bool includeX, bool includeY, int scaleX, int scaleY)
    {
        var imported = AssSubtitleFormat.Parse(File(@"{\an7\pos(120,80)}text", includeX, includeY), 1280, 720);
        var line = Assert.Single(imported.Lines);

        Assert.Equal(new ScenePoint(120 * scaleX, 80 * scaleY), line.Style.Position!.Offset);
        Assert.Equal(20 * scaleY, line.Style.FontSize);
        Assert.Equal((1280, 720), (line.Style.Scaling.ReferenceWidth, line.Style.Scaling.ReferenceHeight));
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
    }

    [Fact]
    public void SourceResolutionPreservesAtomicClipIdentityMasksAndAnimations()
    {
        var imported = AssSubtitleFormat.ParseAtSourceResolution(
            File(@"{\move(0,0,10,10)\t(0,100,\fs40)\clip(0,0,100,100)}text"));
        var line = Assert.Single(imported.Result.Lines);
        var clip = Assert.Single(imported.Result.Clips);

        Assert.Same(line, clip.Line);
        Assert.NotNull(clip.Mask);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.POSITION);
        Assert.Contains(clip.Tracks, track => track.Property == AnimationProperty.FONT_SIZE);
        Assert.Equal("text", line.Text);
        Assert.Equal(20, line.Style.FontSize);
    }

    [Theory]
    [InlineData("PlayResX: 640", "PlayResX: 0")]
    [InlineData("PlayResX: 640", "PlayResX: -1")]
    [InlineData("PlayResX: 640", "PlayResX: invalid")]
    [InlineData("PlayResY: 360", "PlayResY: 0")]
    [InlineData("PlayResY: 360", "PlayResY: -1")]
    [InlineData("PlayResY: 360", "PlayResY: invalid")]
    public void InvalidDeclaredResolutionIsRejectedByBothImportModes(string original, string replacement)
    {
        var source = File("text").Replace(original, replacement, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseAtSourceResolution(source));
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(source));
    }

    [Theory]
    [InlineData(32769, 360)]
    [InlineData(8192, 4320)]
    public void SourceCanvasBudgetDoesNotRestrictExistingResampledImports(int width, int height)
    {
        var source = File("text")
            .Replace("PlayResX: 640", "PlayResX: " + width, StringComparison.Ordinal)
            .Replace("PlayResY: 360", "PlayResY: " + height, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.ParseAtSourceResolution(source));
        var line = Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Lines);
        Assert.Equal((640, 360), (line.Style.Scaling.ReferenceWidth, line.Style.Scaling.ReferenceHeight));
    }

    private static string File(string body, bool includeX = true, bool includeY = true)
    {
        var source = """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, Outline, Shadow, Alignment, MarginV
            Style: Default,Fixture,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,2,2,2,20
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.25,0:00:03.50,Default,,0,0,0,,
            """ + body;
        if (!includeX)
        {
            source = source.Replace("PlayResX: 640\n", "", StringComparison.Ordinal);
        }
        if (!includeY)
        {
            source = source.Replace("PlayResY: 360\n", "", StringComparison.Ordinal);
        }
        return source;
    }
}
