using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssScopedScaleImportTests
{
    private static readonly long[] sampleMilliseconds = [0, 500, 1000, 1999, 2000];

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void WholeLineAxisAnimationKeepsTheOtherAxisStaticInItsOwnTextRange(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var document = Import("{\\t(0,2000,\\" + wholeLineAxis + "200)}a{\\" + localAxis + "200}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, 1 + fraction, offset == 0 ? 1 : 2));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void WholeLineAxisAnimationKeepsTheOtherAxisAnimationInItsOwnTextRange(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var document = Import("{\\t(0,2000,\\" + wholeLineAxis + "200)}a{\\t(0,2000,\\" + localAxis + "300)}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, 1 + fraction, offset == 0 ? 1 : 1 + 2 * fraction));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void InvalidLocalAxisTargetKeepsTheValidWholeLineAxisAnimation(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var parsed = Parse("{\\t(0,2000,\\" + wholeLineAxis + "200)}a{\\t(0,2000,\\" + localAxis + "1000100)}b");
        var document = ImportParsed(parsed);

        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
        AssertOriginalAndExportedScales(document, (_, fraction) => Axes(horizontal, 1 + fraction, 1));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void InvalidLocalStaticAxisKeepsTheValidWholeLineAxisAnimationAndAllowsProjectImport(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var parsed = Parse("{\\t(0,2000,\\" + wholeLineAxis + "200)}a{\\" + localAxis + "1000100}b");
        var document = ImportParsed(parsed);

        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformRange");
        AssertOriginalAndExportedScales(document, (_, fraction) => Axes(horizontal, 1 + fraction, 1));
    }

    [Theory]
    [InlineData("fscx", "fscy", true, "0", 0)]
    [InlineData("fscy", "fscx", false, "0", 0)]
    [InlineData("fscx", "fscy", true, "0.001", 0.00001)]
    [InlineData("fscy", "fscx", false, "0.001", 0.00001)]
    public void ZeroOrSmallWholeLineBasisDoesNotPreventEntranceAlongsideTheOtherAxisLocalScale(string wholeLineAxis,
        string localAxis, bool horizontal, string initialValue, double initialScale)
    {
        var document = Import("{\\" + wholeLineAxis + initialValue + "\\t(0,2000,\\" + wholeLineAxis +
            "200)}a{\\" + localAxis + "200}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, initialScale + (2 - initialScale) * fraction, offset == 0 ? 1 : 2));
    }

    [Theory]
    [InlineData("fscx", "fscy", true, "", 1)]
    [InlineData("fscx", "fscy", true, "150", 1.5)]
    [InlineData("fscy", "fscx", false, "", 1)]
    [InlineData("fscy", "fscx", false, "150", 1.5)]
    public void LocalAxisResetCancelsOnlyItsOwnInheritedAnimation(string localAxis,
        string wholeLineAxis, bool horizontal, string reset, double resetScale)
    {
        var document = Import("{\\" + localAxis + "200\\t(0,2000,\\" + localAxis + "400)" +
            "\\t(0,2000,\\" + wholeLineAxis + "300)}a{\\" + localAxis + reset + "}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, offset == 0 ? 2 + 2 * fraction : resetScale, 1 + 2 * fraction));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void StaticOnlyRunKeepsItsRangeGeometryAfterTheInheritedAxisAnimationIsReset(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var document = Import("{\\t(0,2000,\\" + wholeLineAxis + "200)}a{\\" + wholeLineAxis +
            "\\" + localAxis + "200}b");
        var range = Assert.Single(Assert.Single(document.Subtitles).AnimationRanges,
            range => range.Utf16Start <= 1 && 1 < range.Utf16Start + range.Utf16Length);

        Assert.DoesNotContain(Assert.Single(document.Layers).Tracks, track => track.Target.TextRangeId == range.Id);
        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, offset == 0 ? 1 + fraction : 1, offset == 0 ? 1 : 2));
    }

    [Theory]
    [InlineData("{\\t(0,2000,\\fscx200\\fscy300)}ab")]
    [InlineData("{\\t(0,2000,\\fscx200\\fscy300)}a{\\r\\t(0,2000,\\fscx200\\fscy300)}b")]
    public void IdenticalWholeLineAxesKeepTheExistingSingleTrackConversion(string source)
    {
        var document = Import(source);
        var layer = Assert.Single(document.Layers);
        var track = Assert.Single(layer.Tracks, track => track.Property == AnimationProperty.SCALE);

        Assert.Null(track.Target.TextRangeId);
        Assert.False(track.IsOrdered);
        Assert.Empty(Assert.Single(document.Subtitles).AnimationRanges);
        AssertOriginalAndExportedScales(document, (_, fraction) => new(1 + fraction, 1 + 2 * fraction));

        var copy = RoundTrip(document);
        Assert.Empty(Assert.Single(copy.Subtitles).AnimationRanges);
        Assert.Null(Assert.Single(Assert.Single(copy.Layers).Tracks,
            track => track.Property == AnimationProperty.SCALE).Target.TextRangeId);
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void NonUnitWholeLineBasisAndLocalStaticScaleAreAppliedExactlyOnce(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var document = Import("{\\" + wholeLineAxis + "200\\" + localAxis + "150" +
            "\\t(0,2000,\\" + wholeLineAxis + "400)}a{\\" + localAxis + "300}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, 2 + 2 * fraction, offset == 0 ? 1.5 : 3));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void NonUnitWholeLineBasisAndLocalAnimatedScaleAreAppliedExactlyOnce(string wholeLineAxis,
        string localAxis, bool horizontal)
    {
        var document = Import("{\\" + wholeLineAxis + "200\\" + localAxis + "150" +
            "\\t(0,2000,\\" + wholeLineAxis + "400)}a{\\t(0,2000,\\" + localAxis + "300)}b");

        AssertOriginalAndExportedScales(document, (offset, fraction) =>
            Axes(horizontal, 2 + 2 * fraction, offset == 0 ? 1.5 : 1.5 + 1.5 * fraction));
    }

    [Theory]
    [InlineData("fscx", "fscy", true)]
    [InlineData("fscy", "fscx", false)]
    public void MixedStaticScaleKeepsTheOtherAxesSafeNonUnitOuterBasis(string localAxis,
        string wholeLineAxis, bool horizontal)
    {
        var document = Import("{\\" + localAxis + "150\\" + wholeLineAxis + "75}a{\\" + localAxis + "200}b");

        Assert.Equal(Axes(horizontal, 1, 0.75), Assert.Single(document.Layers).Transform.Scale);
        AssertOriginalAndExportedScales(document, (offset, _) => Axes(horizontal, offset == 0 ? 1.5 : 2, 0.75));
    }

    private static void AssertOriginalAndExportedScales(ProjectDocument document, Func<int, double, ScenePoint> expected)
    {
        AssertScales(document, expected);
        AssertScales(RoundTrip(document), expected);
    }

    private static void AssertScales(ProjectDocument document, Func<int, double, ScenePoint> expected)
    {
        foreach (var milliseconds in sampleMilliseconds)
        {
            foreach (var offset in new[] { 0, 1 })
            {
                var scale = ScaleAt(document, offset, new(milliseconds, 1000));
                var target = expected(offset, milliseconds / 2000d);
                Assert.Equal(target.X, scale.X, 9);
                Assert.Equal(target.Y, scale.Y, 9);
            }
        }
    }

    private static ScenePoint ScaleAt(ProjectDocument document, int offset, MediaTime contentTime)
    {
        var layer = Assert.Single(document.Layers);
        var subtitle = Assert.Single(document.Subtitles);
        var evaluated = SceneEvaluator.EvaluateLayer(layer, subtitle, contentTime + layer.AnimationOffset);
        var scale = evaluated.Transform.Scale;
        Assert.Equal(0, evaluated.Transform.Rotation);
        foreach (var range in evaluated.AnimationRanges)
        {
            if (offset >= range.Utf16Start && offset < range.Utf16Start + range.Utf16Length)
            {
                Assert.Equal(0, range.Rotation);
                scale = new(scale.X * range.Scale.X, scale.Y * range.Scale.Y);
            }
        }
        return scale;
    }

    private static ScenePoint Axes(bool horizontal, double first, double second)
    {
        return horizontal ? new(first, second) : new(second, first);
    }

    private static ProjectDocument RoundTrip(ProjectDocument document)
    {
        return ImportFile(AssSubtitleFormat.Write(document).Text);
    }

    private static ProjectDocument Import(string text)
    {
        return ImportParsed(Parse(text));
    }

    private static AssImportResult Parse(string text)
    {
        return AssSubtitleFormat.Parse($$"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            LayoutResX: 640
            LayoutResY: 360
            WrapStyle: 1
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, ScaleX, ScaleY, Outline, Shadow, Alignment
            Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&HFF000000,&HFF000000,100,100,0,0,2
            [Events]
            Format: Layer, Start, End, Style, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,Default,{{text}}
            """, 640, 360);
    }

    private static ProjectDocument ImportFile(string source)
    {
        return ImportParsed(AssSubtitleFormat.Parse(source, 640, 360));
    }

    private static ProjectDocument ImportParsed(AssImportResult parsed)
    {
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            parsed, "ASS");
        ProjectValidator.Validate(document);
        return document;
    }
}
