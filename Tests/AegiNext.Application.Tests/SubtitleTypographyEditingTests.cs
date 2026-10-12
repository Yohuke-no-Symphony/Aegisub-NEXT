using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTypographyEditingTests
{
    [Fact]
    public void InlineTypographyEditsMergeSelectedFieldsAndUndoAtomically()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "Ae\u0301B");
        editor.ApplySubtitleInlineStyle(id, 0, 4, new() { LetterSpacing = -3.25, FillBlur = 4, StrokeBlur = 7 });
        var before = editor.Snapshot;

        editor.ApplySubtitleInlineStyle(id, 1, 2, new() { FillBlur = 0 });

        var line = Assert.Single(editor.Snapshot.Subtitles);
        var middle = Assert.Single(line.InlineSpans, span => span.Utf16Start == 1);
        Assert.Equal(2, middle.Utf16Length);
        Assert.Equal(-3.25, middle.Style.LetterSpacing);
        Assert.Equal(0, middle.Style.FillBlur);
        Assert.Equal(7, middle.Style.StrokeBlur);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void KaraokeBlurEditsPreserveOtherComponentsAndLayout()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab");
        editor.UpdateSubtitle(id, line => line with
        {
            Style = line.Style with { LetterSpacing = 9, FillBlur = 2, StrokeBlur = 3, WrapMode = SubtitleWrapMode.NO_WRAP },
            Karaoke =
            [
                new(0, 1, new(0), new(1), SceneColor.White),
                new(1, 1, new(1), new(2), SceneColor.White)
            ],
            KaraokeStyleSpans = [new(0, 1, new() { FillBlur = 5, StrokeBlur = 6 })]
        });
        var before = editor.Snapshot;

        editor.ApplySubtitleKaraokeStyleRange(id, 0, 1, new() { FillBlur = 0 });

        var result = Assert.Single(editor.Snapshot.Subtitles);
        var active = KaraokeVisualStyleResolver.ResolveActive(result.Style, result.KaraokeStyle, result.Karaoke[0],
            KaraokeVisualStyleResolver.RangeStyleAt(result, 0, KaraokeVisualState.ACTIVE));
        Assert.Equal(0, active.FillBlur);
        Assert.Equal(6, active.StrokeBlur);
        Assert.Equal(9, active.LetterSpacing);
        Assert.Equal(SubtitleWrapMode.NO_WRAP, active.WrapMode);
        Assert.Same(before.Subtitles[0].Karaoke[1], result.Karaoke[1]);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergingDifferentHighlightSnapshotsPreservesBothBlurComponents(bool inactive)
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "a");
        var second = editor.AddSubtitle(new(2), new(4), "b");
        var firstStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "first", new() { FillBlur = 2, StrokeBlur = 4 });
        var secondStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "second", new() { FillBlur = 3, StrokeBlur = 7 });
        editor.UpdateSubtitle(first, line => line with
        {
            KaraokeStyle = firstStyle,
            Karaoke = inactive ? [] : [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = inactive ? [new(0, 1, new(0), new(1), SceneColor.White)] : []
        });
        editor.UpdateSubtitle(second, line => line with
        {
            KaraokeStyle = secondStyle, KaraokeStyleSpans = [new(0, 1, new() { FillBlur = 0 })],
            Karaoke = inactive ? [] : [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = inactive ? [new(0, 1, new(0), new(1), SceneColor.White)] : []
        });

        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second, "");

        var line = Assert.Single(merged.Subtitles);
        Assert.Null(line.KaraokeStyle);
        var segments = inactive ? line.InactiveKaraoke : line.Karaoke;
        Assert.Equal(2, KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE)!.FillBlur);
        Assert.Equal(4, KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE)!.StrokeBlur);
        Assert.Equal(0, KaraokeVisualStyleResolver.RangeStyleAt(line, 1, KaraokeVisualState.ACTIVE)!.FillBlur);
        Assert.Equal(7, KaraokeVisualStyleResolver.RangeStyleAt(line, 1, KaraokeVisualState.ACTIVE)!.StrokeBlur);
    }

    [Fact]
    public void AdvancedAssTextEditsPreserveUnrepresentedNativeTypographyAndPreciseValues()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(4),
            Style = new() { LetterSpacing = 0.123456789123, FillBlur = 2.123456789123, StrokeBlur = 3.123456789123, WrapMode = SubtitleWrapMode.NO_WRAP },
            InlineSpans = [new(1, 1, new() { LetterSpacing = -2.123456789123, FillBlur = 4.123456789123, StrokeBlur = 5.123456789123 })],
            Karaoke = [new(1, 1, new(1), new(2), SceneColor.White)],
            KaraokeStyleSpans = [new(1, 1,
                new() { FillBlur = 6.123456789123, StrokeBlur = 7.123456789123 },
                new() { FillBlur = 8.123456789123, StrokeBlur = 9.123456789123 })]
        };
        var projection = AssTextProjection.Create(line);

        var changed = AssTextProjection.Apply(line, projection.Source.Replace("}a", "}A", StringComparison.Ordinal)).Line;

        Assert.Equal("Ab", changed.Text);
        Assert.Equal(line.Style, changed.Style);
        Assert.Equal(line.InlineSpans.ToArray(), changed.InlineSpans.ToArray());
        Assert.Equal(line.Karaoke.ToArray(), changed.Karaoke.ToArray());
    }

    [Fact]
    public void AdvancedAssLetterSpacingEditsPreserveIndependentBlurAndWrapping()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(4),
            Style = new()
            {
                LetterSpacing = 0.125, FillBlur = 2.123456789123, StrokeBlur = 3.123456789123,
                ShadowBlur = 4.123456789123, WrapMode = SubtitleWrapMode.NO_WRAP
            }
        };
        var projection = AssTextProjection.Create(line);
        Assert.Contains("\\fsp0.125", projection.Source, StringComparison.Ordinal);

        var changed = AssTextProjection.Apply(line,
            projection.Source.Replace("\\fsp0.125", "\\fsp-3.25", StringComparison.Ordinal)).Line;

        Assert.Equal(line.Style, changed.Style);
        var style = Assert.Single(changed.InlineSpans).Style.ApplyTo(changed.Style);
        Assert.Equal(-3.25, style.LetterSpacing);
        Assert.Equal(line.Style.FillBlur, style.FillBlur);
        Assert.Equal(line.Style.StrokeBlur, style.StrokeBlur);
        Assert.Equal(line.Style.ShadowBlur, style.ShadowBlur);
        Assert.Equal(line.Style.WrapMode, style.WrapMode);
    }

    [Fact]
    public void ExportingIndependentBlurReportsLossWithoutChangingNativeAppearance()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(2),
            Style = new() { LetterSpacing = -2, FillBlur = 4, StrokeBlur = 6, WrapMode = SubtitleWrapMode.NO_WRAP, ShadowBlur = 0 }
        };
        var document = new ProjectDocument
        {
            Subtitles = [line], Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };

        var written = AssSubtitleFormat.Write(document);

        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.SubtitleId == line.Id && diagnostic.Code == "Ass.FillBlur");
        Assert.Contains(SubtitleFormatLossAnalysis.ForSrt(document), diagnostic => diagnostic.Code == "Srt.Appearance");
        Assert.Same(line, Assert.Single(document.Subtitles));
        Assert.Equal(4, line.Style.FillBlur);
        Assert.Equal(6, line.Style.StrokeBlur);
    }
}
