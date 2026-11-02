using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>LRC 与增强型 LRC（逐字 &lt;mm:ss.xx&gt;）交换；逐字时间映射为卡拉 OK 片段。</summary>
public static partial class LrcSubtitleFormat
{
    private const int MAX_ENTRIES = 100000;

    /// <summary>未提供结束标记的最后一行在缺少其他证据时的默认时长。</summary>
    public static MediaTime DefaultLastLineDuration { get; } = new(5);

    /// <summary>解析 LRC/增强型 LRC。时间标签可乱序、可重复；空行时间标签作为结束标记；[offset:] 正值使歌词提前。</summary>
    public static LrcImportResult Parse(string text, SubtitleStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0'))
        {
            throw new InvalidDataException("LRC content contains null characters.");
        }
        ProjectValidator.ValidateText(text);
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>();
        var offsetMilliseconds = 0L;
        var sourceLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('\uFEFF').Split('\n');
        var unrecognized = 0;
        for (var lineNumber = 0; lineNumber < sourceLines.Length; lineNumber++)
        {
            var raw = sourceLines[lineNumber].Trim();
            if (raw.Length == 0)
            {
                continue;
            }
            var id = IdTag().Match(raw);
            if (id.Success && !TimeTag().IsMatch(raw))
            {
                var key = id.Groups[1].Value.ToLowerInvariant();
                var value = id.Groups[2].Value.Trim();
                metadata[key] = value;
                if (key == "offset")
                {
                    if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offsetMilliseconds))
                    {
                        offsetMilliseconds = 0;
                        diagnostics.Add(new("Lrc.InvalidOffset", $"The [offset:{value}] tag is not an integer number of milliseconds and was ignored."));
                    }
                }
                continue;
            }

            var tags = new List<MediaTime>();
            var position = 0;
            Match match;
            while ((match = TimeTag().Match(raw, position)).Success && match.Index == position)
            {
                tags.Add(ParseTime(match));
                position += match.Length;
                while (position < raw.Length && raw[position] is ' ' or '\t')
                {
                    position++;
                }
            }
            if (tags.Count == 0)
            {
                unrecognized++;
                continue;
            }
            var body = raw[position..];
            var parsed = ParseWords(body);
            foreach (var time in tags)
            {
                if (entries.Count >= MAX_ENTRIES)
                {
                    throw new InvalidDataException("LRC entries exceed the count limit.");
                }
                entries.Add(new(time, parsed.Text, parsed.Words, tags[0], lineNumber));
            }
        }
        if (unrecognized > 0)
        {
            diagnostics.Add(new("Lrc.UnrecognizedLines", $"{unrecognized} line(s) without a time tag or a recognized ID tag were skipped."));
        }
        var stored = metadata.Keys.Where(key => key is not "offset" and not "length").ToArray();
        if (stored.Length > 0)
        {
            diagnostics.Add(new("Lrc.Metadata", $"LRC ID tags ({string.Join(", ", stored.Select(key => "[" + key + ":]"))}) are not stored in the project and will not be written on export."));
        }

        var offset = new MediaTime(offsetMilliseconds, 1000);
        var negative = false;
        MediaTime Effective(MediaTime time)
        {
            var value = time - offset;
            if (value < MediaTime.Zero)
            {
                negative = true;
                return MediaTime.Zero;
            }
            return value;
        }

        // 稳定排序：同一时间按源顺序；结束时间取下一个严格更晚的时间点。
        var ordered = entries.Select(entry => entry with { Time = Effective(entry.Time) })
            .OrderBy(entry => entry.Time).ThenBy(entry => entry.SourceLine).ToList();
        MediaTime? declaredLength = null;
        if (metadata.TryGetValue("length", out var lengthText))
        {
            var lengthMatch = LengthValue().Match(lengthText);
            if (lengthMatch.Success)
            {
                declaredLength = ParseTime(lengthMatch) - offset;
            }
        }

        var result = ImmutableArray.CreateBuilder<SubtitleLine>();
        var droppedKaraoke = 0;
        var clampedKaraoke = 0;
        var defaultedLast = false;
        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];
            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                continue;
            }
            var start = entry.Time;
            MediaTime end;
            var next = ordered.Skip(index + 1).FirstOrDefault(candidate => candidate.Time > start);
            if (next is not null)
            {
                end = next.Time;
            }
            else
            {
                var shift = entry.Time - Effective(entry.FirstTag);
                var lastWord = entry.Words.Count > 0 && entry.Words[^1].End is { } wordEnd ? Effective(wordEnd) + shift : (MediaTime?)null;
                if (lastWord is { } wordTime && wordTime > start)
                {
                    end = wordTime;
                }
                else if (declaredLength is { } length && length > start)
                {
                    end = length;
                }
                else
                {
                    end = start + DefaultLastLineDuration;
                    defaultedLast = true;
                }
            }

            var line = new SubtitleLine { Start = start, End = end, Text = entry.Text, Style = style ?? new() };
            if (entry.Words.Count > 0)
            {
                var shift = entry.Time - Effective(entry.FirstTag);
                var segments = ImmutableArray.CreateBuilder<KaraokeSegment>();
                foreach (var word in entry.Words)
                {
                    var wordStart = Effective(word.Start) + shift - start;
                    var wordEnd = (word.End is { } explicitEnd ? Effective(explicitEnd) + shift : end) - start;
                    var duration = end - start;
                    if (wordStart < MediaTime.Zero || wordEnd > duration)
                    {
                        clampedKaraoke++;
                        wordStart = wordStart < MediaTime.Zero ? MediaTime.Zero : wordStart > duration ? duration : wordStart;
                        wordEnd = wordEnd > duration ? duration : wordEnd;
                    }
                    if (wordStart >= wordEnd)
                    {
                        continue;
                    }
                    segments.Add(new(word.Utf16Start, word.Utf16Length, wordStart, wordEnd, KaraokeVisualStyleResolver.DefaultHighlightColor));
                }
                var timed = line with { Karaoke = segments.ToImmutable() };
                try
                {
                    ProjectValidator.ValidateSubtitleKaraoke(timed);
                    line = timed;
                }
                catch (InvalidDataException)
                {
                    droppedKaraoke++;
                }
            }
            result.Add(line);
        }
        if (negative)
        {
            diagnostics.Add(new("Lrc.NegativeTime", "Some times became negative after applying [offset:] and were clamped to zero."));
        }
        if (clampedKaraoke > 0)
        {
            diagnostics.Add(new("Lrc.KaraokeClamped", $"{clampedKaraoke} word timestamp(s) fell outside their line interval and were clamped to it."));
        }
        if (droppedKaraoke > 0)
        {
            diagnostics.Add(new("Lrc.KaraokeDropped", $"Word timing of {droppedKaraoke} line(s) could not be represented (out-of-order tags or split characters) and was imported as ordinary text."));
        }
        if (defaultedLast)
        {
            diagnostics.Add(new("Lrc.LastLineDuration", "The last lyric line has no end marker, word end time, or [length:] tag; it was given a default duration of 5 seconds."));
        }
        return new(result.ToImmutable(), diagnostics.ToImmutable(), metadata.ToImmutable());
    }

    /// <summary>导出 LRC；enhanced 时把卡拉 OK 片段写为逐字时间标签。间隙与末行写空行结束标记，时间向下取整到百分之一秒。</summary>
    public static SubtitleFormatWriteResult Write(ProjectDocument document, MediaTime timeOffset = default, bool enhanced = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = SubtitleFormatLossAnalysis.ForLrc(document, enhanced).ToBuilder();
        var text = Write(document.Subtitles, timeOffset, enhanced, diagnostics);
        return new(text, diagnostics.ToImmutable());
    }

    /// <summary>仅输出字幕行；诊断写入可选集合。</summary>
    public static string Write(IEnumerable<SubtitleLine> lines, MediaTime timeOffset, bool enhanced,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var ordered = lines.OrderBy(line => line.Start).Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToList();
        if (ordered.Count > MAX_ENTRIES)
        {
            throw new InvalidDataException("Subtitle exchange content exceeds the size limit.");
        }
        var output = new StringBuilder();
        var joined = false;
        for (var index = 0; index < ordered.Count; index++)
        {
            var line = ordered[index];
            ProjectValidator.ValidateText(line.Text);
            var start = line.Start + timeOffset;
            var end = line.End + timeOffset;
            if (start < MediaTime.Zero)
            {
                throw new InvalidDataException($"LRC subtitle {line.Id} has a negative external start time of {start}. Adjust the interval or playback origin; it will not be clipped automatically.");
            }
            if (start >= end)
            {
                throw new InvalidDataException("LRC does not support invalid intervals.");
            }
            var startCs = Centiseconds(start);
            output.Append(FormatTag('[', startCs, ']'));
            var body = new StringBuilder();
            if (enhanced && !line.Karaoke.IsEmpty)
            {
                var cursor = 0;
                long? pending = null;
                foreach (var segment in line.Karaoke)
                {
                    var between = line.Text[cursor..segment.Utf16Start];
                    var segmentStart = Centiseconds(start + segment.Start);
                    if (pending is { } previousEnd && previousEnd != segmentStart)
                    {
                        body.Append(FormatTag('<', previousEnd, '>'));
                    }
                    body.Append(between);
                    body.Append(FormatTag('<', segmentStart, '>'));
                    body.Append(line.Text, segment.Utf16Start, segment.Utf16Length);
                    cursor = segment.Utf16Start + segment.Utf16Length;
                    pending = Centiseconds(start + segment.End);
                }
                body.Append(FormatTag('<', pending!.Value, '>'));
                body.Append(line.Text[cursor..]);
            }
            else if (enhanced && !string.IsNullOrWhiteSpace(line.Text))
            {
                // 没有逐字计时的行也写句首与句尾标签，使增强型 LRC 的每一行都带 <> 时间。
                var lineNext = index + 1 < ordered.Count ? ordered[index + 1].Start + timeOffset : (MediaTime?)null;
                var tail = lineNext is { } nextTime && nextTime < end ? nextTime : end;
                body.Append(FormatTag('<', startCs, '>')).Append(line.Text).Append(FormatTag('<', Centiseconds(tail), '>'));
            }
            else
            {
                body.Append(line.Text);
            }
            var flattened = body.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (flattened.Contains('\n'))
            {
                joined = true;
                flattened = string.Join(' ', flattened.Split('\n').Select(part => part.Trim()).Where(part => part.Length > 0));
            }
            output.Append(flattened).Append('\n');

            var next = index + 1 < ordered.Count ? ordered[index + 1] : null;
            var nextStart = next is null ? (MediaTime?)null : next.Start + timeOffset;
            if (nextStart is { } following && following < end)
            {
                diagnostics?.Add(new("Lrc.Overlap", "LRC shows one line at a time. This line overlaps the next one and ends when the next line starts.", SubtitleId: line.Id));
            }
            else if (!enhanced && (nextStart is null || Centiseconds(nextStart.Value) > Centiseconds(end)))
            {
                output.Append(FormatTag('[', Centiseconds(end), ']')).Append('\n');
            }
        }
        if (joined)
        {
            diagnostics?.Add(new("Lrc.LineBreaks", "LRC lines cannot contain line breaks. Multi-line subtitles were joined with spaces."));
        }
        return output.ToString();
    }

    private static long Centiseconds(MediaTime time)
    {
        return time.ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value;
    }

    private static string FormatTag(char open, long centiseconds, char close)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{open}{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}{close}");
    }

    private static (string Text, List<Word> Words) ParseWords(string body)
    {
        var words = new List<Word>();
        var builder = new StringBuilder();
        var matches = WordTag().Matches(body);
        if (matches.Count == 0)
        {
            return (body.Trim(), words);
        }
        var position = 0;
        MediaTime? current = null;
        var currentStart = 0;
        foreach (Match match in matches)
        {
            builder.Append(body, position, match.Index - position);
            var time = ParseTime(match);
            if (current is { } open)
            {
                words.Add(new(currentStart, builder.Length - currentStart, open, time));
            }
            current = time;
            currentStart = builder.Length;
            position = match.Index + match.Length;
        }
        builder.Append(body, position, body.Length - position);
        if (current is { } last)
        {
            words.Add(new(currentStart, builder.Length - currentStart, last, null));
        }
        var full = builder.ToString();
        var lead = full.Length - full.TrimStart().Length;
        var text = full.Trim();
        var result = new List<Word>();
        foreach (var word in words)
        {
            var from = Math.Max(word.Utf16Start - lead, 0);
            var to = Math.Min(word.Utf16Start + word.Utf16Length - lead, text.Length);
            if (to > from)
            {
                result.Add(word with { Utf16Start = from, Utf16Length = to - from });
            }
        }
        return (text, result);
    }

    private static MediaTime ParseTime(Match match)
    {
        var minutes = long.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = long.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success ? match.Groups["f"].Value : "";
        var milliseconds = fraction.Length switch
        {
            0 => 0,
            1 => long.Parse(fraction, CultureInfo.InvariantCulture) * 100,
            2 => long.Parse(fraction, CultureInfo.InvariantCulture) * 10,
            _ => long.Parse(fraction, CultureInfo.InvariantCulture)
        };
        return new(checked((minutes * 60 + seconds) * 1000 + milliseconds), 1000);
    }

    private const string TIME = @"(?<m>[0-9]{1,4}):(?<s>[0-5]?[0-9])(?:[.:](?<f>[0-9]{1,3}))?";

    [GeneratedRegex(@"\[" + TIME + @"\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"<" + TIME + @">", RegexOptions.CultureInvariant)]
    private static partial Regex WordTag();

    [GeneratedRegex(@"^" + TIME + @"$", RegexOptions.CultureInvariant)]
    private static partial Regex LengthValue();

    [GeneratedRegex(@"^\[([A-Za-z#][A-Za-z0-9_#-]*):(.*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex IdTag();

    private sealed record Entry(MediaTime Time, string Text, List<Word> Words, MediaTime FirstTag, int SourceLine);

    private sealed record Word(int Utf16Start, int Utf16Length, MediaTime Start, MediaTime? End);
}

/// <summary>LRC 导入结果：字幕行、转换诊断和原始 ID 标签。</summary>
public sealed record LrcImportResult(ImmutableArray<SubtitleLine> Lines,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics, ImmutableDictionary<string, string> Metadata);
