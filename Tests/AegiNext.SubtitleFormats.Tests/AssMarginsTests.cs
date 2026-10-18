using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssMarginsTests
{
    [Theory]
    [InlineData(0, 0, 0, 13, 41, 27)]
    [InlineData(7, 0, 0, 7, 41, 27)]
    [InlineData(0, 9, 0, 13, 9, 27)]
    [InlineData(0, 0, 11, 13, 41, 11)]
    [InlineData(7, 9, 11, 7, 9, 11)]
    public void DialogueOverridesEachMarginIndependentlyAndZeroInherits(int left, int right, int vertical,
        int expectedLeft, int expectedRight, int expectedVertical)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File(left, right, vertical, "text"), 1280, 1080).Lines);
        Assert.Equal(new SubtitleMargins(expectedLeft * 2, expectedRight * 2, expectedVertical * 3), line.Style.Margins);
        Assert.Null(line.Style.Position);
    }

    [Theory]
    [InlineData("\\an7", TextAlignment.TOP_LEFT)]
    [InlineData("\\an5", TextAlignment.MIDDLE_CENTER)]
    [InlineData("\\an3", TextAlignment.BOTTOM_RIGHT)]
    [InlineData("\\a5", TextAlignment.TOP_LEFT)]
    [InlineData("\\a10", TextAlignment.MIDDLE_CENTER)]
    [InlineData("\\a3", TextAlignment.BOTTOM_RIGHT)]
    public void AlignmentTagsKeepAsymmetricStyleAndEventMargins(string tag, TextAlignment alignment)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File(7, 0, 11, "{" + tag + "}text"), 640, 360).Lines);
        Assert.Equal(alignment, line.Style.Alignment);
        Assert.Equal(new SubtitleMargins(7, 41, 11), line.Style.Margins);
        Assert.Null(line.Style.Position);
    }

    [Theory]
    [InlineData("\\an7\\pos(100,80)")]
    [InlineData("\\pos(100,80)\\an7")]
    public void ExplicitPositionRetainsMarginsAndUsesWholeCanvasCoordinates(string tags)
    {
        var line = Assert.Single(AssSubtitleFormat.Parse(File(7, 9, 11, "{" + tags + "}text"), 1280, 1080).Lines);
        Assert.Equal(new SubtitleMargins(14, 18, 33), line.Style.Margins);
        Assert.Equal(new ScenePoint(0, 0), line.Style.Position!.Anchor);
        Assert.Equal(new ScenePoint(0, 0), line.Style.Position.Pivot);
        Assert.Equal(new ScenePoint(200, 240), line.Style.Position.Offset);
    }

    [Theory]
    [InlineData(TextAlignment.TOP_LEFT)]
    [InlineData(TextAlignment.TOP_CENTER)]
    [InlineData(TextAlignment.TOP_RIGHT)]
    [InlineData(TextAlignment.MIDDLE_LEFT)]
    [InlineData(TextAlignment.MIDDLE_CENTER)]
    [InlineData(TextAlignment.MIDDLE_RIGHT)]
    [InlineData(TextAlignment.BOTTOM_LEFT)]
    [InlineData(TextAlignment.BOTTOM_CENTER)]
    [InlineData(TextAlignment.BOTTOM_RIGHT)]
    public void ExportPreservesAllThreeMarginsWithoutMakingAutomaticPositionExplicit(TextAlignment alignment)
    {
        var line = new SubtitleLine
        {
            Text = "margin test", End = new(3),
            Style = new() { Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }, Alignment = alignment, Margins = new(13, 41, 27) }
        };
        var document = SubtitleFormatTestDocument.Create(line) with { Width = 640, Height = 360 };
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal));
        Assert.EndsWith(",13,41,27,1", style.TrimEnd('\r'), StringComparison.Ordinal);
        var dialogue = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Dialogue: ", StringComparison.Ordinal)).Split(',', 10);
        Assert.Equal(new[] { "0", "0", "0" }, dialogue.Skip(5).Take(3));
        Assert.DoesNotContain("\\pos", written.Text, StringComparison.Ordinal);
        Assert.Contains("WrapStyle: 1", written.Text, StringComparison.Ordinal);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Lines);
        Assert.Equal(line.Style.Margins, imported.Style.Margins);
        Assert.Equal(alignment, imported.Style.Alignment);
        Assert.Null(imported.Style.Position);
    }

    [Fact]
    public void DuplicateAlignmentAndStyleResetKeepTheDialogueMargins()
    {
        var parsed = AssSubtitleFormat.Parse(File(7, 9, 11, "{\\an7\\an3\\r}text"), 640, 360);
        var line = Assert.Single(parsed.Lines);
        Assert.Equal(TextAlignment.TOP_LEFT, line.Style.Alignment);
        Assert.Equal(new SubtitleMargins(7, 9, 11), line.Style.Margins);
        Assert.Null(line.Style.Position);
        Assert.Single(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.DuplicatePlacement");
    }

    [Fact]
    public void DistinctHorizontalMarginsShareOneStyleAndRemainDistinctDialogueValues()
    {
        var first = new SubtitleLine { Text = "first", End = new(3), Style = new() { Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }, Margins = new(13, 41, 27) } };
        var second = new SubtitleLine { Text = "second", Start = new(3), End = new(6), Style = new() { Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 }, Margins = new(41, 13, 27) } };
        var document = SubtitleFormatTestDocument.Create(first, second) with { Width = 640, Height = 360 };
        var written = AssSubtitleFormat.Write(document);
        var style = Assert.Single(written.Text.Split('\n'), row => row.StartsWith("Style: ", StringComparison.Ordinal));
        Assert.EndsWith(",0,0,27,1", style.TrimEnd('\r'), StringComparison.Ordinal);
        var dialogues = written.Text.Split('\n').Where(row => row.StartsWith("Dialogue: ", StringComparison.Ordinal))
            .Select(row => row.Split(',', 10)).ToArray();
        Assert.Equal(new[] { "13", "41", "0" }, dialogues[0].Skip(5).Take(3));
        Assert.Equal(new[] { "41", "13", "0" }, dialogues[1].Skip(5).Take(3));
        Assert.All(dialogues, row => Assert.Equal("Default", row[3]));
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
        var imported = AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Lines;
        Assert.Equal(document.Subtitles.Select(line => line.Style.Margins), imported.Select(line => line.Style.Margins));
    }

    [Fact]
    public void ProjectSourceAlignmentEditPreservesNativeMarginsAndPosition()
    {
        var original = new SubtitleLine
        {
            Text = "text", End = new(3),
            Style = new() { Margins = new(13, 41, 27), Position = new() { Offset = new(17, -23) } }
        };
        var source = AssTextProjection.Create(original).Source.Replace("\\an2", "\\an7", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(original, source).Line;
        Assert.Equal(TextAlignment.TOP_LEFT, edited.Style.Alignment);
        Assert.Equal(original.Style.Margins, edited.Style.Margins);
        Assert.Equal(original.Style.Position, edited.Style.Position);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void InvalidDialogueMarginRejectsImport(int left, int right, int vertical)
    {
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Parse(File(left, right, vertical, "text"), 640, 360));
    }

    private static string File(int left, int right, int vertical, string text)
    {
        return $$"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, Outline, Shadow, Alignment, MarginL, MarginR, MarginV
            Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,2,2,2,13,41,27
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:03.00,Default,,{{left}},{{right}},{{vertical}},,{{text}}
            """;
    }
}
