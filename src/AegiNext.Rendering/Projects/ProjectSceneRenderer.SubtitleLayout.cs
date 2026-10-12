using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private const int WRAP_PROBE_GRAPHEMES = 32;
    /// <summary>返回与实际渲染共用的不可变 run、字素、选区和光标几何；快照不持有原生资源。</summary>
    public SubtitleTextLayout MeasureSubtitleTextLayout(ProjectDocument document, SubtitleLine subtitle)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subtitle);
        Prepare(document);
        return Layout(document, subtitle).Snapshot;
    }

    /// <summary>测量已经求值的字幕，动画字距与当前画面及命中几何一致。</summary>
    public SubtitleTextLayout MeasureSubtitleTextLayout(ProjectDocument document, EvaluatedLayer layer)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Subtitle is null)
        {
            throw new ArgumentException("Layout measurement requires a subtitle layer.", nameof(layer));
        }
        Prepare(document);
        return VisibleLayout(layer, Layout(document, layer).Snapshot);
    }

    private SubtitleLayout Layout(ProjectDocument document, EvaluatedLayer layer)
    {
        layer = CanvasLayer(document, layer);
        var subtitle = TypographySubtitle(layer);
        return Layout(document, subtitle, animatedLayerId: ReferenceEquals(subtitle, layer.Subtitle) ? null : layer.Source.Id);
    }

    private SubtitleLayout Layout(ProjectDocument document, SubtitleLine subtitle, double? letterSpacing = null, Guid? animatedLayerId = null)
    {
        subtitle = CanvasSubtitle(document, subtitle);
        var key = new SubtitleLayoutKey(subtitle, document.Width, document.Height, letterSpacing);
        if (layouts.TryGet(key, out var existing))
        {
            return existing;
        }
        var lines = new List<SubtitleLayoutLine>();
        var offset = 0;
        try
        {
            foreach (var paragraph in subtitle.Text.Split('\n'))
            {
                var text = paragraph.TrimEnd('\r');
                if (text.Length == 0)
                {
                    lines.Add(new(string.Empty, offset, [], (float)subtitle.Style.FontSize, 0));
                    offset += paragraph.Length + 1;
                    continue;
                }
                var direction = text.EnumerateRunes().Any(value => value.Value is >= 0x0590 and <= 0x08ff)
                    ? TextDirection.RIGHT_TO_LEFT : TextDirection.LEFT_TO_RIGHT;
                var boundaries = StringInfo.ParseCombiningCharacters(text);
                int[] graphemeEnds = [.. boundaries.Skip(1), text.Length];
                var natural = subtitle.Style.WrapMode == SubtitleWrapMode.NATURAL ? SubtitleLineBreaks.Create(text, boundaries) : null;
                var negativeSpacing = letterSpacing.HasValue ? letterSpacing < 0 : subtitle.Style.LetterSpacing < 0 ||
                    subtitle.InlineSpans.Any(span => span.Style.LetterSpacing < 0);
                var measure = negativeSpacing && subtitle.Style.WrapMode != SubtitleWrapMode.NO_WRAP
                    ? MeasureWrap(document, subtitle, text, offset, boundaries, direction, letterSpacing) : null;
                var begin = 0;
                while (begin < text.Length)
                {
                    var runs = ShapeWrappedRuns(document, subtitle, text, offset, begin, graphemeEnds, direction,
                        letterSpacing, natural, measure, out var end);
                    lines.Add(new(text[begin..end], offset + begin, runs, (float)runs.Max(run => run.Style.FontSize),
                        runs.Sum(run => run.AdvanceWidth)));
                    begin = end;
                }
                offset += paragraph.Length + 1;
            }
            var result = PositionLayout(document, subtitle, lines);
            layouts.Add(key, result, animatedLayerId);
            return result;
        }
        catch
        {
            foreach (var line in lines)
            {
                DisposeRuns(line.Runs);
            }
            throw;
        }
    }

    private ImmutableArray<SubtitleLayoutRun> ShapeWrappedRuns(ProjectDocument document, SubtitleLine subtitle,
        string text, int offset, int begin, int[] boundaries, TextDirection direction, double? letterSpacing,
        SubtitleLineBreaks? natural, SubtitleWrapMeasure? measure, out int end)
    {
        if (subtitle.Style.WrapMode == SubtitleWrapMode.NO_WRAP)
        {
            end = text.Length;
            return ShapeRuns(document, subtitle, text[begin..], offset + begin, direction, letterSpacing);
        }
        var candidates = natural?.Preferred ?? boundaries;
        var available = (float)Math.Max(1, document.Width - subtitle.Style.Margins.Left - subtitle.Style.Margins.Right);
        if (measure is not null)
        {
            end = measure.FindEnd(begin, candidates, available, out var fits);
            var allowed = candidates;
            if (!fits && natural is not null)
            {
                allowed = natural.Emergency;
                end = measure.FindEnd(begin, allowed, available, out _);
            }
            var verified = Verify(allowed, end, out end, out var actualFits);
            if (actualFits || natural is null || ReferenceEquals(allowed, natural.Emergency))
            {
                return verified;
            }
            DisposeRuns(verified);
            end = measure.FindEnd(begin, natural.Emergency, available, out _);
            return Verify(natural.Emergency, end, out end, out _);
        }
        var selected = Probe(candidates, out end, out var fitsPreferred);
        if (fitsPreferred || natural is null)
        {
            return selected;
        }
        DisposeRuns(selected);
        return Probe(natural.Emergency, out end, out _);

        ImmutableArray<SubtitleLayoutRun> Verify(int[] allowed, int desiredEnd, out int selectedEnd, out bool fits)
        {
            var result = ShapeRuns(document, subtitle, text[begin..desiredEnd], offset + begin, direction, letterSpacing);
            selectedEnd = desiredEnd;
            fits = MeasureRunInkWidth(result, direction) <= available;
            if (fits)
            {
                return result;
            }
            try
            {
                var first = Array.BinarySearch(allowed, begin);
                first = first < 0 ? ~first : first + 1;
                var low = first;
                var high = Array.BinarySearch(allowed, desiredEnd) - 1;
                while (low <= high)
                {
                    var middle = low + (high - low) / 2;
                    var candidate = ShapeRuns(document, subtitle, text[begin..allowed[middle]], offset + begin, direction, letterSpacing);
                    if (MeasureRunInkWidth(candidate, direction) <= available)
                    {
                        DisposeRuns(result);
                        result = candidate;
                        selectedEnd = allowed[middle];
                        fits = true;
                        low = middle + 1;
                    }
                    else
                    {
                        DisposeRuns(candidate);
                        high = middle - 1;
                    }
                }
                if (!fits && selectedEnd != allowed[first])
                {
                    DisposeRuns(result);
                    result = [];
                    selectedEnd = allowed[first];
                    result = ShapeRuns(document, subtitle, text[begin..selectedEnd], offset + begin, direction, letterSpacing);
                    fits = MeasureRunInkWidth(result, direction) <= available;
                }
                return result;
            }
            catch
            {
                DisposeRuns(result);
                throw;
            }
        }

        ImmutableArray<SubtitleLayoutRun> Probe(int[] allowed, out int selectedEnd, out bool fits)
        {
            var startCandidate = Array.BinarySearch(allowed, begin);
            startCandidate = startCandidate < 0 ? ~startCandidate : startCandidate + 1;
            var low = startCandidate;
            var high = Math.Min(allowed.Length - 1, startCandidate + WRAP_PROBE_GRAPHEMES - 1);
            while (true)
            {
                var candidateEnd = allowed[high];
                var candidate = ShapeRuns(document, subtitle, text[begin..candidateEnd], offset + begin, direction, letterSpacing);
                if (candidate.Sum(run => run.AdvanceWidth) <= available)
                {
                    if (high == allowed.Length - 1)
                    {
                        selectedEnd = candidateEnd;
                        fits = true;
                        return candidate;
                    }
                    low = high;
                    high = Math.Min(allowed.Length - 1, startCandidate + (high - startCandidate + 1) * 2 - 1);
                    DisposeRuns(candidate);
                    continue;
                }
                DisposeRuns(candidate);
                high--;
                break;
            }
            high = Math.Max(low, high);
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                var candidateEnd = allowed[middle];
                var candidate = ShapeRuns(document, subtitle, text[begin..candidateEnd], offset + begin, direction, letterSpacing);
                var width = candidate.Sum(run => run.AdvanceWidth);
                DisposeRuns(candidate);
                if (width <= available)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }
            selectedEnd = allowed[low];
            var result = ShapeRuns(document, subtitle, text[begin..selectedEnd], offset + begin, direction, letterSpacing);
            fits = result.Sum(run => run.AdvanceWidth) <= available;
            return result;
        }
    }

    private static float MeasureRunInkWidth(ImmutableArray<SubtitleLayoutRun> runs, TextDirection direction)
    {
        var ink = SKRect.Empty;
        var advance = 0f;
        var total = runs.Sum(run => run.AdvanceWidth);
        foreach (var run in runs)
        {
            var bounds = RunInkBounds(run);
            if (!bounds.IsEmpty)
            {
                bounds.Offset(direction == TextDirection.RIGHT_TO_LEFT ? total - advance - run.Shape.AdvanceWidth : advance, 0);
                ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
            }
            advance += run.AdvanceWidth;
        }
        return ink.IsEmpty ? Math.Abs(total) : ink.Width;
    }

    private ImmutableArray<SubtitleLayoutRun> ShapeRuns(ProjectDocument document, SubtitleLine subtitle,
        string text, int offset, TextDirection direction, double? letterSpacing = null)
    {
        var runs = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
        try
        {
            var start = 0;
            while (start < text.Length)
            {
                var style = subtitle.Style;
                var end = text.Length;
                var spanIndex = FindInlineSpan(subtitle.InlineSpans, offset + start);
                if (spanIndex < subtitle.InlineSpans.Length)
                {
                    var span = subtitle.InlineSpans[spanIndex];
                    if (span.Utf16Start <= offset + start)
                    {
                        style = span.Style.ApplyTo(style);
                        end = Math.Min(end, span.Utf16Start + span.Utf16Length - offset);
                    }
                    else
                    {
                        end = Math.Min(end, span.Utf16Start - offset);
                    }
                }
                if (letterSpacing.HasValue)
                {
                    style = style with { LetterSpacing = letterSpacing.Value };
                }
                var piece = text[start..end];
                ShapeFontRuns(document, style, piece, offset + start, direction, runs);
                start = end;
            }
            for (var index = 0; index < runs.Count - 1; index++)
            {
                runs[index] = runs[index] with { TrailingSpacing = (float)runs[index].Style.LetterSpacing };
            }
            return runs.ToImmutable();
        }
        catch
        {
            DisposeRuns(runs);
            throw;
        }
    }

    private SubtitleWrapMeasure MeasureWrap(ProjectDocument document, SubtitleLine subtitle, string text, int offset,
        int[] boundaries, TextDirection direction, double? letterSpacing)
    {
        var runs = ShapeRuns(document, subtitle, text, offset, direction, letterSpacing);
        try
        {
            var bounds = new SKRect[boundaries.Length];
            var advance = 0f;
            var total = runs.Sum(run => run.AdvanceWidth);
            foreach (var run in runs)
            {
                var x = direction == TextDirection.RIGHT_TO_LEFT ? total - advance - run.Shape.AdvanceWidth : advance;
                var geometries = MeasureGraphemes(run, new(x, 0), 0, 0, 1, includeSpacing: false);
                var clusters = run.Shape.Clusters.ToArray().OrderBy(cluster => cluster.Utf16Start).ToArray();
                var clusterIndex = 0;
                foreach (var grapheme in geometries)
                {
                    var index = Array.BinarySearch(boundaries, grapheme.Utf16Start - offset);
                    var local = grapheme.Utf16Start - run.Utf16Offset;
                    while (clusterIndex + 1 < clusters.Length && clusters[clusterIndex + 1].Utf16Start <= local)
                    {
                        clusterIndex++;
                    }
                    var cluster = clusters[clusterIndex];
                    var clusterEnd = clusterIndex + 1 < clusters.Length ? clusters[clusterIndex + 1].Utf16Start : run.Text.Length;
                    var measured = grapheme.Bounds;
                    if (!cluster.InkBounds.IsEmpty && local == cluster.Utf16Start && local + grapheme.Utf16Length == clusterEnd)
                    {
                        measured.Left = Math.Min(measured.Left, x + cluster.InkBounds.Left);
                        measured.Right = Math.Max(measured.Right, x + cluster.InkBounds.Right);
                    }
                    bounds[index] = measured;
                }
                advance += run.AdvanceWidth;
            }
            return new(boundaries, bounds, text.Length);
        }
        finally
        {
            DisposeRuns(runs);
        }
    }

    private static int FindInlineSpan(ImmutableArray<SubtitleInlineSpan> spans, int offset)
    {
        var low = 0;
        var high = spans.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (spans[middle].Utf16Start + spans[middle].Utf16Length <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    private static void DisposeRuns(IEnumerable<SubtitleLayoutRun> runs)
    {
        foreach (var run in runs)
        {
            run.Shape.Dispose();
        }
    }

    private static SubtitleLayout PositionLayout(ProjectDocument document, SubtitleLine subtitle,
        List<SubtitleLayoutLine> lines)
    {
        var style = subtitle.Style;
        var horizontal = (int)style.Alignment % 3;
        var vertical = (int)style.Alignment / 3;
        var blockWidth = style.TextAlign.HasValue ? MeasureTextBlockWidth(lines) : 0;
        var blockLeft = horizontal switch
        {
            0 => (float)style.Margins.Left,
            1 => (float)((style.Margins.Left + document.Width - style.Margins.Right - blockWidth) / 2),
            _ => document.Width - (float)style.Margins.Right - blockWidth
        };
        var blockHeight = lines[^1].FontSize;
        for (var index = 0; index < lines.Count - 1; index++)
        {
            blockHeight += lines[index].FontSize * (float)style.LineHeight;
        }
        var top = vertical switch
        {
            0 => (float)style.Margins.Vertical,
            1 => (document.Height - blockHeight) / 2,
            _ => document.Height - (float)style.Margins.Vertical - blockHeight
        };
        var ink = SKRect.Empty;
        var rowTop = top;
        var geometries = ImmutableArray.CreateBuilder<SubtitleTextRunGeometry>();
        var graphemes = ImmutableArray.CreateBuilder<SubtitleGraphemeGeometry>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var localInk = SKRect.Empty;
            var positioned = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
            var advance = 0f;
            var rtl = !line.Runs.IsEmpty && line.Runs[0].Direction == TextDirection.RIGHT_TO_LEFT;
            foreach (var run in line.Runs)
            {
                var runX = rtl ? line.AdvanceWidth - advance - run.Shape.AdvanceWidth : advance;
                var bounds = RunInkBounds(run);
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(runX, 0);
                    localInk = localInk.IsEmpty ? bounds : SKRect.Union(localInk, bounds);
                }
                positioned.Add(run with { Position = new(runX, 0) });
                advance += run.AdvanceWidth;
            }
            var left = localInk.IsEmpty ? 0 : localInk.Left;
            var width = localInk.IsEmpty ? line.AdvanceWidth : localInk.Width;
            var x = horizontal switch
            {
                0 => (float)style.Margins.Left - left,
                1 => (float)((style.Margins.Left + document.Width - style.Margins.Right - width) / 2) - left,
                _ => document.Width - (float)style.Margins.Right - width - left
            };
            if (style.TextAlign is { } textAlign && (int)textAlign != horizontal)
            {
                var rowOffset = textAlign switch
                {
                    SubtitleTextAlignment.LEFT => 0,
                    SubtitleTextAlignment.CENTER => (blockWidth - width) / 2,
                    _ => blockWidth - width
                };
                x = blockLeft + rowOffset - left;
            }
            var baseline = new SKPoint(x, rowTop + line.FontSize);
            for (var runIndex = 0; runIndex < positioned.Count; runIndex++)
            {
                var run = positioned[runIndex];
                var position = new SKPoint(baseline.X + run.Position.X, baseline.Y);
                var runGraphemes = MeasureGraphemes(run, position, index, rowTop,
                    rowTop + line.FontSize * (float)style.LineHeight);
                positioned[runIndex] = run with { Position = position, Graphemes = runGraphemes };
                var bounds = RunInkBounds(run);
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(position);
                }
                geometries.Add(new(run.Utf16Offset, run.Text.Length, index, run.Style, position, bounds)
                {
                    ResolvedFontFamily = run.ResolvedFontFamily,
                    ResolvedFontVariant = run.ResolvedFontVariant
                });
                graphemes.AddRange(runGraphemes);
                if (!bounds.IsEmpty)
                {
                    ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
                }
            }
            lines[index] = line with { Position = baseline, Runs = positioned.ToImmutable() };
            rowTop += line.FontSize * (float)style.LineHeight;
        }
        AddNewlineGeometry(subtitle.Text, lines, graphemes, style.LineHeight);
        var orderedGraphemes = graphemes.OrderBy(value => value.Utf16Start).ToImmutableArray();
        PopulateKaraoke(subtitle, lines, orderedGraphemes);
        var hasInk = !ink.IsEmpty;
        if (!hasInk)
        {
            var width = Math.Max(1, lines.Max(line => line.AdvanceWidth));
            var x = horizontal switch
            {
                0 => (float)style.Margins.Left,
                1 => (float)((style.Margins.Left + document.Width - style.Margins.Right - width) / 2),
                _ => document.Width - (float)style.Margins.Right - width
            };
            ink = new(x, top, x + width, top + blockHeight);
        }
        var normalized = style.Position ?? SubtitlePosition.FromAlignment(style.Alignment, style.Margins);
        var resolvedPivot = LayerPositionGeometry.LocalPivot(normalized, new(ink.Left, ink.Top), new(ink.Width, ink.Height));
        var resolvedBase = LayerPositionGeometry.BasePosition(normalized, new(document.Width, document.Height));
        var pivot = new SKPoint((float)resolvedPivot.X, (float)resolvedPivot.Y);
        var basePosition = style.Position is not null
            ? new SKPoint((float)resolvedBase.X, (float)resolvedBase.Y)
            : new SKPoint((float)resolvedBase.X, pivot.Y);
        var first = lines[0];
        var emptyCaret = new SKRect(first.Position.X, first.Position.Y - first.FontSize,
            first.Position.X + 1, first.Position.Y + first.FontSize * ((float)style.LineHeight - 1));
        var snapshot = new SubtitleTextLayout(subtitle.Text, ink, basePosition, pivot, hasInk,
            geometries.ToImmutable(), orderedGraphemes, emptyCaret);
        return new(lines, ink, basePosition, pivot, hasInk, snapshot);
    }

    private static float MeasureTextBlockWidth(List<SubtitleLayoutLine> lines)
    {
        var width = 0f;
        foreach (var line in lines)
        {
            var ink = SKRect.Empty;
            var advance = 0f;
            var rtl = !line.Runs.IsEmpty && line.Runs[0].Direction == TextDirection.RIGHT_TO_LEFT;
            foreach (var run in line.Runs)
            {
                var bounds = RunInkBounds(run);
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(rtl ? line.AdvanceWidth - advance - run.Shape.AdvanceWidth : advance, 0);
                    ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
                }
                advance += run.AdvanceWidth;
            }
            width = Math.Max(width, ink.Width);
        }
        return width > 0 ? width : Math.Max(1, lines.Max(line => line.AdvanceWidth));
    }

    private static ImmutableArray<SubtitleGraphemeGeometry> MeasureGraphemes(SubtitleLayoutRun run, SKPoint position,
        int lineIndex, float top, float bottom, bool includeSpacing = true)
    {
        var starts = StringInfo.ParseCombiningCharacters(run.Text);
        var clusters = run.Shape.Clusters.ToArray().ToDictionary(cluster => cluster.Utf16Start);
        var keys = clusters.Keys.Order().ToArray();
        var result = ImmutableArray.CreateBuilder<SubtitleGraphemeGeometry>();
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var keyIndex = Array.BinarySearch(keys, start);
            if (keyIndex < 0)
            {
                keyIndex = Math.Max(0, ~keyIndex - 1);
            }
            var clusterStart = keys[keyIndex];
            var clusterEnd = keyIndex + 1 < keys.Length ? keys[keyIndex + 1] : run.Text.Length;
            var firstMember = BoundaryIndex(starts, clusterStart);
            var memberCount = Math.Max(1, BoundaryIndex(starts, clusterEnd) - firstMember);
            var member = index - firstMember;
            var rtl = run.Direction == TextDirection.RIGHT_TO_LEFT;
            var left = clusters[clusterStart].Start;
            var right = includeSpacing ? clusters[clusterStart].End : clusters[clusterStart].ContentEnd;
            if (includeSpacing && keyIndex == keys.Length - 1)
            {
                if (rtl)
                {
                    left -= run.TrailingSpacing;
                }
                else
                {
                    right += run.TrailingSpacing;
                }
            }
            var step = (right - left) / memberCount;
            var leading = position.X + left + step * (rtl ? memberCount - member : member);
            var trailing = leading + (rtl ? -step : step);
            var length = (index + 1 < starts.Length ? starts[index + 1] : run.Text.Length) - start;
            result.Add(new(run.Utf16Offset + start, length, lineIndex,
                new(Math.Min(leading, trailing), top, Math.Max(leading, trailing), bottom),
                new(leading, top, leading + 1, bottom), new(trailing, top, trailing + 1, bottom)));
        }
        return result.ToImmutable();
    }

    private static void AddNewlineGeometry(string text, List<SubtitleLayoutLine> lines,
        ImmutableArray<SubtitleGraphemeGeometry>.Builder graphemes, double lineHeight)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var length = (index + 1 < starts.Length ? starts[index + 1] : text.Length) - start;
            if (text[start] is not ('\r' or '\n'))
            {
                continue;
            }
            var lineIndex = 0;
            while (lineIndex + 1 < lines.Count && lines[lineIndex + 1].Utf16Offset <= start)
            {
                lineIndex++;
            }
            var line = lines[lineIndex];
            var next = lines[Math.Min(lineIndex + 1, lines.Count - 1)];
            var leading = line.Runs.IsEmpty ? line.Position.X : line.Runs[^1].Graphemes[^1].TrailingCaret.Left;
            var top = line.Position.Y - line.FontSize;
            var bottom = top + line.FontSize * (float)lineHeight;
            var nextTop = next.Position.Y - next.FontSize;
            graphemes.Add(new(start, length, lineIndex, new(leading, top, leading, bottom),
                new(leading, top, leading + 1, bottom),
                new(next.Position.X, nextTop, next.Position.X + 1, nextTop + next.FontSize * (float)lineHeight)));
        }
    }

    private static int BoundaryIndex(int[] boundaries, int value)
    {
        var index = Array.BinarySearch(boundaries, value);
        return index < 0 ? ~index : index;
    }

    private static void PopulateKaraoke(SubtitleLine subtitle, List<SubtitleLayoutLine> lines,
        ImmutableArray<SubtitleGraphemeGeometry> graphemes)
    {
        if (subtitle.Karaoke.IsEmpty)
        {
            return;
        }
        var starts = graphemes.Select(glyph => glyph.Utf16Start).ToArray();
        var cumulative = new double[graphemes.Length + 1];
        for (var index = 0; index < graphemes.Length; index++)
        {
            cumulative[index + 1] = cumulative[index] + graphemes[index].Bounds.Width;
        }
        var totals = subtitle.Karaoke.Select(segment => (float)(cumulative[BoundaryIndex(starts, segment.Utf16Start + segment.Utf16Length)] -
            cumulative[BoundaryIndex(starts, segment.Utf16Start)])).ToArray();
        var advances = new float[subtitle.Karaoke.Length];
        var segmentIndex = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var runs = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
            foreach (var run in line.Runs)
            {
                while (segmentIndex < subtitle.Karaoke.Length && subtitle.Karaoke[segmentIndex].Utf16Start +
                    subtitle.Karaoke[segmentIndex].Utf16Length <= run.Utf16Offset)
                {
                    segmentIndex++;
                }
                var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeSpan>();
                var runStarts = run.Graphemes.Select(glyph => glyph.Utf16Start).ToArray();
                for (var current = segmentIndex; current < subtitle.Karaoke.Length; current++)
                {
                    var segment = subtitle.Karaoke[current];
                    if (segment.Utf16Start >= run.Utf16Offset + run.Text.Length)
                    {
                        break;
                    }
                    var first = BoundaryIndex(runStarts, segment.Utf16Start);
                    var end = BoundaryIndex(runStarts, segment.Utf16Start + segment.Utf16Length);
                    if (first == end)
                    {
                        continue;
                    }
                    var bounds = run.Graphemes[first].Bounds;
                    var width = 0f;
                    for (var glyph = first; glyph < end; glyph++)
                    {
                        bounds = SKRect.Union(bounds, run.Graphemes[glyph].Bounds);
                        width += run.Graphemes[glyph].Bounds.Width;
                    }
                    spans.Add(new(segment, bounds, advances[current], totals[current],
                        run.Direction == TextDirection.RIGHT_TO_LEFT, first == 0, end == run.Graphemes.Length));
                    advances[current] += width;
                }
                runs.Add(run with { Karaoke = spans.ToImmutable() });
            }
            lines[index] = line with { Runs = runs.ToImmutable() };
        }
    }

    private static SKRect RunInkBounds(SubtitleLayoutRun run)
    {
        var bounds = run.Shape.InkBounds;
        foreach (var decoration in Decorations(run))
        {
            bounds = bounds.IsEmpty ? decoration : SKRect.Union(bounds, decoration);
        }
        return bounds;
    }

    private static IEnumerable<SKRect> Decorations(SubtitleLayoutRun run)
    {
        var metrics = run.Shape.FontMetrics;
        if (run.Style.Underline)
        {
            var position = metrics.UnderlinePosition ?? (float)run.Style.FontSize * 0.1f;
            var thickness = Math.Max(1, metrics.UnderlineThickness ?? (float)run.Style.FontSize * 0.05f);
            yield return new(Math.Min(0, run.Shape.AdvanceWidth), position, Math.Max(0, run.Shape.AdvanceWidth), position + thickness);
        }
        if (run.Style.Strikethrough)
        {
            var position = metrics.StrikeoutPosition ?? -(float)run.Style.FontSize * 0.3f;
            var thickness = Math.Max(1, metrics.StrikeoutThickness ?? (float)run.Style.FontSize * 0.05f);
            yield return new(Math.Min(0, run.Shape.AdvanceWidth), position, Math.Max(0, run.Shape.AdvanceWidth), position + thickness);
        }
    }
}
