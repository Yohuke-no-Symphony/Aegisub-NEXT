using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssBlurRangeExportTests
{
    [Theory]
    [InlineData(0, 60, 2, 120, true)]
    [InlineData(2, 60, 2, 120, true)]
    [InlineData(0, 120, 0.5, 60, false)]
    [InlineData(2, 120, 0.5, 60, false)]
    [InlineData(0, 50, 2, 100, false)]
    [InlineData(2, 50, 2, 100, false)]
    public void StaticBlurDiagnosesTheActuallySerializedAmountAfterAppearanceScaling(double strokeWidth,
        double nativeAssUnits, double scale, double expectedAmount, bool loss)
    {
        var sigma = nativeAssUnits * AssBlurConversion.SigmaPerUnit;
        var line = Line(strokeWidth) with
        {
            Style = Line(strokeWidth).Style with
            {
                FillBlur = strokeWidth == 0 ? sigma : 0,
                StrokeBlur = strokeWidth > 0 ? sigma : 0
            }
        };
        var document = Document(line, scale);

        var written = AssSubtitleFormat.Write(document);

        Assert.Equal(expectedAmount, Assert.Single(BlurValues(written.Text)), 8);
        Assert.Equal(loss, written.Diagnostics.Any(diagnostic => diagnostic.Code == "Ass.BlurRange"));
        Assert.Same(line, Assert.Single(document.Subtitles));
        Assert.Equal(sigma, strokeWidth == 0 ? line.Style.FillBlur : line.Style.StrokeBlur);
    }

    [Theory]
    [InlineData(AnimationProperty.FILL_BLUR, false, false)]
    [InlineData(AnimationProperty.FILL_BLUR, false, true)]
    [InlineData(AnimationProperty.FILL_BLUR, true, false)]
    [InlineData(AnimationProperty.FILL_BLUR, true, true)]
    [InlineData(AnimationProperty.STROKE_BLUR, false, false)]
    [InlineData(AnimationProperty.STROKE_BLUR, false, true)]
    [InlineData(AnimationProperty.STROKE_BLUR, true, false)]
    [InlineData(AnimationProperty.STROKE_BLUR, true, true)]
    public void ContinuousBlurDiagnosesBothInitialAndTargetWithoutClampingTheOrderedTarget(AnimationProperty property,
        bool scoped, bool initialExceedsLimit)
    {
        var initialAmount = initialExceedsLimit ? 120d : 40d;
        var targetAmount = initialExceedsLimit ? 40d : 120d;
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = Line(property == AnimationProperty.FILL_BLUR ? 0 : 2) with
        {
            AnimationRanges = scoped ? [range] : []
        };
        var initial = initialAmount * AssBlurConversion.SigmaPerUnit / 2;
        var target = targetAmount * AssBlurConversion.SigmaPerUnit / 2;
        var track = new AnimationTrack(new AnimationTrackTarget(property, TextRangeId: scoped ? range.Id : null), [])
        {
            InitialValue = initial,
            Transforms = [new(Guid.NewGuid(), new(0), new(2), target)]
        };
        var document = Document(line, 2) with
        {
            Layers = [Document(line, 2).Layers[0] with { Tracks = [track] }]
        };
        ProjectValidator.Validate(document);

        var written = AssSubtitleFormat.Write(document);

        Assert.Contains(initialAmount, BlurValues(written.Text));
        Assert.Contains(targetAmount, BlurValues(written.Text));
        Assert.Contains("\\t(0,2000,1,\\blur" + AssFormatValues.Number(targetAmount) + ")", written.Text,
            StringComparison.Ordinal);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        Assert.Same(track, Assert.Single(document.Layers[0].Tracks));
        Assert.Equal(initial, track.InitialValue!.Value.Scalar);
        Assert.Equal(target, Assert.Single(track.Transforms).Value.Scalar);
    }

    [Fact]
    public void AdvancedNativeProjectionDoesNotApplyTheExternalAssBlurLimit()
    {
        var line = Line(0) with { Style = Line(0).Style with { FillBlur = 300, ShadowBlur = 300 } };

        var projection = AssTextProjection.Create(line);
        var changed = AssTextProjection.Apply(line, projection.Source.Replace("\\fs20", "\\fs24", StringComparison.Ordinal));

        Assert.Equal(300, Assert.Single(BlurValues(projection.Source)));
        Assert.DoesNotContain(projection.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        Assert.DoesNotContain(changed.Diagnostics, diagnostic => diagnostic.Code == "Ass.BlurRange");
        var style = changed.Line.InlineSpans.IsEmpty ? changed.Line.Style : changed.Line.InlineSpans[0].Style.ApplyTo(changed.Line.Style);
        Assert.Equal(300, style.FillBlur);
        Assert.Equal(300, style.ShadowBlur);
        Assert.Equal(24, style.FontSize);
        Assert.Equal(300, line.Style.FillBlur);
        Assert.Equal(300, line.Style.ShadowBlur);
    }

    private static SubtitleLine Line(double strokeWidth)
    {
        return new()
        {
            Text = "ab",
            End = new(2),
            Style = new()
            {
                FontFamily = "Noto Sans",
                FontSize = 20,
                StrokeWidth = strokeWidth,
                ShadowOffset = new(0, 0),
                ShadowColor = SceneColor.Transparent,
                ShadowBlur = 0,
                WrapMode = SubtitleWrapMode.NATURAL
            }
        };
    }

    private static ProjectDocument Document(SubtitleLine line, double scale)
    {
        var document = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Subtitles = [line],
            Layers = [new()
            {
                SubtitleId = line.Id,
                End = line.End,
                Transform = new() { Scale = new(scale, scale) }
            }]
        };
        ProjectValidator.Validate(document);
        return document;
    }

    private static double[] BlurValues(string source)
    {
        return Regex.Matches(source, @"\\blur([-+\d.]+)")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
    }
}
