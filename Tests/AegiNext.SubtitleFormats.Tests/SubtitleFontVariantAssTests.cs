using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class SubtitleFontVariantAssTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("color")]
    [InlineData("karaoke")]
    public void SourceEditsPreserveBaseAndInlineVariants(string edit)
    {
        var line = Line();
        var source = AssTextProjection.Create(line).Source;
        var changed = edit switch
        {
            "text" => source.Replace("B", "XB", StringComparison.Ordinal),
            "color" => source.Replace("A", "{\\1c&H00FF00&}A", StringComparison.Ordinal),
            _ => source.Replace("\\kf100", "\\kf150", StringComparison.Ordinal)
        };
        Assert.NotEqual(source, changed);
        var result = AssTextProjection.Apply(line, changed).Line;
        Assert.Equal(line.Style.FontVariant, result.Style.FontVariant);
        var offset = result.Text.IndexOf('B');
        var effective = result.InlineSpans.First(span => span.Utf16Start <= offset && span.Utf16Start + span.Utf16Length > offset).Style.ApplyTo(result.Style);
        Assert.Equal(line.InlineSpans[0].Style.FontVariant, effective.FontVariant);
    }

    [Theory]
    [InlineData("\\fnOther Sans")]
    [InlineData("\\b1")]
    [InlineData("\\i1")]
    public void ChangingFontSemanticsClearsTheVariantOnlyInTheEditedRange(string tag)
    {
        var line = Line() with { InlineSpans = [], Karaoke = [] };
        var source = AssTextProjection.Create(line).Source;
        var result = AssTextProjection.Apply(line, source.Replace("B", "{" + tag + "}B", StringComparison.Ordinal)).Line;
        Assert.Equal(line.Style.FontVariant, result.Style.FontVariant);
        var effective = Assert.Single(result.InlineSpans).Style.ApplyTo(result.Style);
        Assert.Null(effective.FontVariant);
    }

    [Fact]
    public void AssExportReportsBaseAndInlineVariantLossWithoutChangingTheProject()
    {
        var line = Line();
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        var exported = AssSubtitleFormat.Write(document);
        Assert.Contains(exported.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontVariant" && diagnostic.SubtitleId == line.Id);
        Assert.Equal(line, document.Subtitles[0]);
        Assert.Contains("Example Sans", exported.Text, StringComparison.Ordinal);
    }

    private static SubtitleLine Line() => new()
    {
        Text = "AB", End = new(4),
        Style = new() { FontFamily = "Example Sans", FontVariant = new() { Name = "SemiBold", Weight = 600 } },
        InlineSpans = [new(1, 1, new() { FontVariant = new() { Name = "Black", Weight = 900 }, Bold = true })],
        Karaoke = [new(0, 2, new(0), new(1), SceneColor.White)]
    };
}
