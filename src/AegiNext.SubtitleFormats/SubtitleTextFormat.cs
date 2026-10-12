using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>严格、文化无关的纯文本及 SRT 交换；不隐式解释富文本标记。</summary>
public static partial class SubtitleTextFormat
{
    /// <summary>解析 SRT，保留多行内容；无效条目整体拒绝，不悄悄跳过。</summary>
    public static ImmutableArray<SubtitleLine> ParseSrt(string text, SubtitleStyle? style = null)
    {
        CheckText(text);
        var lines = Normalize(text).TrimStart('\uFEFF').Split('\n');
        var result = ImmutableArray.CreateBuilder<SubtitleLine>();
        var index = 0;
        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            if (!int.TryParse(lines[index++], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0 || index >= lines.Length)
            {
                throw new InvalidDataException($"SRT 第 {index} 行缺少正整数序号。");
            }

            var match = TimestampLine().Match(lines[index++]);
            if (!match.Success)
            {
                throw new InvalidDataException($"SRT 第 {index} 行时间格式无效。");
            }

            var start = ParseTime(match.Groups[1].Value);
            var end = ParseTime(match.Groups[2].Value);
            if (start >= end)
            {
                throw new InvalidDataException("SRT 结束时间必须晚于开始时间。");
            }

            var content = new List<string>();
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                content.Add(lines[index++]);
            }

            if (content.Count == 0 || result.Count >= 100000)
            {
                throw new InvalidDataException("SRT 条目为空或数量过大。");
            }

            result.Add(new() { Start = start, End = end, Text = string.Join('\n', content), Style = style ?? new() });
        }

        return result.ToImmutable();
    }

    /// <summary>输出 SRT；开始向下、结束向上量化到毫秒，保留正持续时间并明确拒绝负时间。</summary>
    public static string WriteSrt(IEnumerable<SubtitleLine> lines, MediaTime timeOffset = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new StringBuilder();
        var index = 0;
        foreach (var line in lines.OrderBy(line => line.Start))
        {
            CheckText(line.Text);
            var start = line.Start + timeOffset;
            var end = line.End + timeOffset;
            if (start < MediaTime.Zero)
            {
                throw new InvalidDataException($"SRT 字幕 {line.Id} 的外部开始时间 {start} 为负；请调整区间或播放零点，不会自动裁剪。");
            }
            if (start >= end || string.IsNullOrWhiteSpace(line.Text) ||
                Normalize(line.Text).Split('\n').Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("SRT 不支持负时间、空行文本或无效区间。");
            }

            result.AppendLine((++index).ToString(CultureInfo.InvariantCulture));
            result.Append(FormatTime(start, MediaTimeRounding.FLOOR)).Append(" --> ")
                .AppendLine(FormatTime(end, MediaTimeRounding.CEILING));
            result.AppendLine(Normalize(line.Text)).AppendLine();
            if (index > 100000)
            {
                throw new InvalidDataException("字幕交换内容过大。");
            }
        }

        return result.ToString();
    }

    /// <summary>每个非空文本行生成一个字幕；时长和起点必须显式或使用两秒／零的默认值。</summary>
    public static ImmutableArray<SubtitleLine> ImportText(string text, MediaTime? lineDuration = null,
        MediaTime? start = null, SubtitleStyle? style = null)
    {
        CheckText(text);
        var duration = lineDuration ?? new MediaTime(2);
        if (duration <= MediaTime.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lineDuration));
        }

        var position = start ?? MediaTime.Zero;
        var result = ImmutableArray.CreateBuilder<SubtitleLine>();
        foreach (var content in Normalize(text).TrimStart('\uFEFF').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (result.Count >= 100000)
            {
                throw new InvalidDataException("文本行数量过大。");
            }

            result.Add(new() { Start = position, End = position + duration, Text = content, Style = style ?? new() });
            position += duration;
        }

        return result.ToImmutable();
    }

    /// <summary>仅输出字幕文本；时间和样式不属于 TXT 格式。</summary>
    public static string WriteText(IEnumerable<SubtitleLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = string.Join('\n', lines.Select(line => Normalize(line.Text)));
        CheckText(result);
        return result;
    }

    private static MediaTime ParseTime(string value)
    {
        var parts = value.Split(':', ',');
        var hours = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var seconds = int.Parse(parts[2], CultureInfo.InvariantCulture);
        var milliseconds = int.Parse(parts[3], CultureInfo.InvariantCulture);
        return new(checked(((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds), 1000);
    }

    private static string FormatTime(MediaTime time, MediaTimeRounding rounding)
    {
        var milliseconds = time.ToTimestamp(new(1, 1000), rounding).Value;
        return string.Create(CultureInfo.InvariantCulture,
            $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00},{milliseconds % 1000:000}");
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static void CheckText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0'))
        {
            throw new InvalidDataException("字幕交换内容包含空字符。");
        }

        ProjectValidator.ValidateText(text);
    }

    [GeneratedRegex(@"^([0-9]{2,9}:[0-5][0-9]:[0-5][0-9],[0-9]{3})\s+-->\s+([0-9]{2,9}:[0-5][0-9]:[0-5][0-9],[0-9]{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampLine();
}
