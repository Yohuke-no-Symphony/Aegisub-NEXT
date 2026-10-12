using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssProjectionCompatibilityTests
{
    [Fact]
    public void TextOnlyEditKeepsNativeDefaultBlurWithoutReportingAnExchangeLoss()
    {
        var line = new SubtitleLine { Text = "Original", Style = new() { FillBlur = 1.25, StrokeBlur = 2.5 } };
        Assert.Equal(2, line.Style.ShadowBlur);
        var projection = AssTextProjection.Create(line);
        var edited = AssTextProjection.Apply(line, projection.Source.Replace("Original", "Changed", StringComparison.Ordinal));
        Assert.Empty(edited.Diagnostics);
        Assert.Equal("Changed", edited.Line.Text);
        Assert.Equal(line.Style, edited.Line.Style);
        Assert.True(line.InlineSpans.SequenceEqual(edited.Line.InlineSpans));
    }

    [Fact]
    public void ChangedBlurTagStillReportsTheActualCompatibilityLossAtItsSourceSpan()
    {
        var line = new SubtitleLine { Text = "Original", Style = new() { FillBlur = 1.25, StrokeBlur = 2.5 } };
        var source = AssTextProjection.Create(line).Source.Replace("\\blur2", "\\blur3", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source);
        var loss = Assert.Single(edited.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.ShadowBlur"));
        Assert.Equal("\\blur3", source.Substring(loss.SourceStart, loss.SourceLength));
        Assert.Equal(3, Assert.Single(edited.Line.InlineSpans).Style.ShadowBlur);
        var style = Assert.Single(edited.Line.InlineSpans).Style.ApplyTo(edited.Line.Style);
        Assert.Equal(1.25, style.FillBlur);
        Assert.Equal(2.5, style.StrokeBlur);
        Assert.Equal(2, line.Style.ShadowBlur);
    }

    [Fact]
    public void ShiftedUnchangedTagSpansKeepTheOriginalNativeProperty()
    {
        var line = new SubtitleLine { Text = "Original" };
        var source = AssTextProjection.Create(line).Source;
        var edited = AssTextProjection.Apply(line, "前" + source);
        Assert.Empty(edited.Diagnostics);
        Assert.Equal("前Original", edited.Line.Text);
        Assert.Equal(line.Style, edited.Line.Style);
        Assert.True(line.InlineSpans.SequenceEqual(edited.Line.InlineSpans));
    }

    [Fact]
    public void MultipleTextEditsPreserveTheDifferentUntouchedBlurTags()
    {
        var line = new SubtitleLine { Text = "ab", InlineSpans = [new(1, 1, new() { ShadowBlur = 3 })] };
        var source = AssTextProjection.Create(line).Source.Replace("}a", "}A", StringComparison.Ordinal)
            .Replace("}b", "}B", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source);
        Assert.Equal("AB", edited.Line.Text);
        Assert.Empty(edited.Diagnostics);
        Assert.Equal(line.Style, edited.Line.Style);
        Assert.True(line.InlineSpans.SequenceEqual(edited.Line.InlineSpans));
    }

    [Fact]
    public void ARepeatedBlurTokenDoesNotHideARealChangeOnAnotherTextRange()
    {
        var line = new SubtitleLine { Text = "ab", InlineSpans = [new(1, 1, new() { ShadowBlur = 0 })] };
        var source = AssTextProjection.Create(line).Source.Replace("\\blur0", "\\blur2", StringComparison.Ordinal);
        var edited = AssTextProjection.Apply(line, source);
        var loss = Assert.Single(edited.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.ShadowBlur"));
        Assert.Equal("\\blur2", source.Substring(loss.SourceStart, loss.SourceLength));
        Assert.True(loss.SourceStart > source.IndexOf("\\blur2", StringComparison.Ordinal));
        Assert.Equal(2, Assert.Single(edited.Line.InlineSpans).Style.ShadowBlur);
    }

    [Fact]
    public void ExactNativeProjectionHasNoEditingLossEvenWhenAssExportWouldReportOne()
    {
        var line = new SubtitleLine { Text = "Original" };
        var projection = AssTextProjection.Create(line);
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShadowBlur");
        var edited = AssTextProjection.Apply(line, projection.Source);
        Assert.Same(line, edited.Line);
        Assert.Empty(edited.Diagnostics);
    }
}
