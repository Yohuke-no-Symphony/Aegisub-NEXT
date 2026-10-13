using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>解析版本 1 和版本 2 的声明式特效文本，拒绝未知指令和超过预算的输入。</summary>
public static class EffectScriptParser
{
    public const int MAXIMUM_SOURCE_CHARACTERS = 262144;
    private const string NUMBER = @"[+-]?(?:\d+(?:\.\d+)?|\.\d+)";
    private static readonly Regex headerPattern = Pattern(@"^effect\s+""(?<id>[a-z][a-z0-9.-]{0,63})""\s+version\s+1$");
    private static readonly Regex versionTwoHeaderPattern = Pattern(@"^effect\s+""[a-z][a-z0-9.-]{0,63}""\s+version\s+2$");
    private static readonly Regex segmentPattern = Pattern(@"^segment\s+(?<name>[a-z][a-z0-9.-]{0,63})\s+(?<kind>fixed|flex)\s+(?<amount>\S+)$");
    private static readonly Regex nodePattern = Pattern(@"^mask-node\(\s*(?<contour>\d+)\s*,\s*(?<node>\d+)\s*\)\.(?<part>position|in-handle|out-handle)$");
    private static readonly Regex powerPattern = Pattern(@"^power\(\s*(?<exponent>" + NUMBER + @")\s*\)$");
    private static readonly Regex keyframePattern = Pattern(@"^at\s+(?<progress>\S+)\s+(?<property>mask-node\(\s*\d+\s*,\s*\d+\s*\)\.[a-z-]+|[a-z][a-z-]*)\s+(?<value>base|(?:offset|factor)?\(\s*" +
        NUMBER + @"\s*(?:,\s*" + NUMBER + @"\s*)?\)|rgba\(\s*" + NUMBER + @"\s*,\s*" + NUMBER + @"\s*,\s*" +
        NUMBER + @"\s*,\s*" + NUMBER + @"\s*\)|" + NUMBER + @")(?:\s+(?<interpolation>[a-z-]+(?:\(\s*" + NUMBER + @"\s*\))?))?$");
    private static readonly Regex durationPattern = Pattern(@"^(?<number>\d+(?:\.\d{1,6})?)(?<unit>ms|s)$");

    /// <summary>从 UTF-8 文本内容读取脚本；不读取文件、程序集或运行环境。</summary>
    public static EffectScript Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MAXIMUM_SOURCE_CHARACTERS)
        {
            throw new EffectScriptException("脚本超过 256 Ki 字符预算。");
        }

        foreach (var sourceLine in source.TrimStart('\uFEFF').Split('\n'))
        {
            var text = sourceLine.Split('#', 2)[0].Trim();
            if (text.Length == 0)
            {
                continue;
            }
            if (versionTwoHeaderPattern.IsMatch(text))
            {
                return EffectScriptV2Parser.Parse(source);
            }
            break;
        }

        string? id = null;
        EffectScriptShortClipPolicy? policy = null;
        var segments = ImmutableArray.CreateBuilder<EffectScriptSegment>();
        var keys = ImmutableArray.CreateBuilder<EffectScriptKeyframe>();
        string? segmentName = null;
        MediaTime? fixedDuration = null;
        var flexWeight = 0m;
        var segmentLine = 0;
        var segmentColumn = 1;
        var keyCount = 0;
        var lines = source.TrimStart('\uFEFF').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Split('#', 2)[0].Trim();
            var line = index + 1;
            var column = lines[index].TakeWhile(char.IsWhiteSpace).Count() + 1;
            if (text.Length == 0)
            {
                continue;
            }

            if (id is null)
            {
                var header = headerPattern.Match(text);
                if (!header.Success)
                {
                    throw new EffectScriptException("首条指令应为 effect \"稳定标识\" version 1。", line, column);
                }

                id = header.Groups["id"].Value;
                continue;
            }

            if (policy is null)
            {
                policy = text switch
                {
                    "short-clip compress" => EffectScriptShortClipPolicy.COMPRESS,
                    "short-clip reject" => EffectScriptShortClipPolicy.REJECT,
                    _ => throw new EffectScriptException("应显式指定 short-clip compress 或 short-clip reject。", line, column)
                };
                continue;
            }

            if (segmentName is null)
            {
                var segment = segmentPattern.Match(text);
                if (!segment.Success)
                {
                    throw new EffectScriptException("应开始 segment 名称 fixed 时长 或 segment 名称 flex 权重。", line, column);
                }

                if (segments.Count >= EffectScriptValidator.MAXIMUM_SEGMENTS)
                {
                    throw new EffectScriptException("时间段数量超过预算。", line, column);
                }

                segmentName = segment.Groups["name"].Value;
                var amount = segment.Groups["amount"].Value;
                var amountColumn = column + segment.Groups["amount"].Index;
                fixedDuration = segment.Groups["kind"].Value == "fixed" ? ParseDuration(amount, line, amountColumn) : null;
                flexWeight = fixedDuration.HasValue ? 0 : ParseDecimal(amount, line, amountColumn);
                segmentLine = line;
                segmentColumn = column;
                continue;
            }

            if (text == "end")
            {
                segments.Add(new(segmentName, fixedDuration, flexWeight, keys.ToImmutable(), segmentLine, segmentColumn));
                keys.Clear();
                segmentName = null;
                continue;
            }

            var key = keyframePattern.Match(text);
            if (!key.Success)
            {
                throw new EffectScriptException("关键帧应为 at 段内位置 属性 值 [插值方式]；段以 end 结束。", line, column);
            }

            if (++keyCount > EffectScriptValidator.MAXIMUM_KEYFRAMES)
            {
                throw new EffectScriptException("关键帧数量超过预算。", line, column);
            }

            var property = ParseProperty(key.Groups["property"].Value, line, column + key.Groups["property"].Index);
            var curve = ParseInterpolation(key.Groups["interpolation"].Value, line, column + key.Groups["interpolation"].Index);
            keys.Add(new(ParseDecimal(key.Groups["progress"].Value, line, column + key.Groups["progress"].Index),
                property.Property,
                ParseValue(key.Groups["value"].Value, line, column + key.Groups["value"].Index),
                curve.Interpolation, line, column)
            {
                NodeSelector = property.Selector, Exponent = curve.Exponent
            });
        }

        if (id is null || policy is null)
        {
            throw new EffectScriptException("缺少脚本标识或短片段规则。");
        }

        if (segmentName is not null)
        {
            throw new EffectScriptException("时间段缺少 end。", segmentLine, segmentColumn);
        }

        var result = new EffectScript(id, policy.Value, segments.ToImmutable());
        EffectScriptValidator.Validate(result);
        return result;
    }

    internal static MediaTime ParseDuration(string text, int line, int column, bool allowZero = false)
    {
        var match = durationPattern.Match(text);
        if (!match.Success)
        {
            throw new EffectScriptException("固定时长必须使用 s 或 ms，最多六位小数。", line, column);
        }

        var value = ParseDecimal(match.Groups["number"].Value, line, column);
        if (match.Groups["unit"].Value == "ms")
        {
            value /= 1000;
        }

        if (value < 0 || !allowZero && value == 0 || value > EffectScriptValidator.MAXIMUM_FIXED_SECONDS)
        {
            throw new EffectScriptException(allowZero ? "启动延迟及错开间隔必须非负且不超过 24 小时。" : "固定段时长必须大于零且不超过 24 小时。", line, column);
        }

        return EffectScriptTiming.Scale(new(1), value);
    }

    internal static decimal ParseDecimal(string text, int line, int column)
    {
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0 && text.Length - dot - 1 > 6 ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw new EffectScriptException("应使用最多六位小数的非负十进制数。", line, column);
        }

        return value;
    }

    internal static (EffectScriptProperty Property, EffectScriptNodeSelector? Selector) ParseProperty(string text, int line, int column)
    {
        var node = nodePattern.Match(text);
        if (node.Success)
        {
            if (!int.TryParse(node.Groups["contour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var contourNumber) ||
                !int.TryParse(node.Groups["node"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var nodeNumber) ||
                contourNumber is < 1 or > 10000 || nodeNumber is < 1 or > 10000)
            {
                throw new EffectScriptException("mask-node 使用 1 到 10000 的轮廓及节点序号。", line, column);
            }

            var part = node.Groups["part"].Value switch
            {
                "position" => EffectScriptProperty.MASK_NODE_POSITION,
                "in-handle" => EffectScriptProperty.MASK_NODE_IN_HANDLE,
                _ => EffectScriptProperty.MASK_NODE_OUT_HANDLE
            };
            return (part, new(contourNumber, nodeNumber));
        }

        var property = text switch
        {
            "position" => EffectScriptProperty.POSITION,
            "scale" => EffectScriptProperty.SCALE,
            "rotation" => EffectScriptProperty.ROTATION,
            "rotation-x" => EffectScriptProperty.ROTATION_X,
            "rotation-y" => EffectScriptProperty.ROTATION_Y,
            "perspective-distance" => EffectScriptProperty.PERSPECTIVE_DISTANCE,
            "opacity" => EffectScriptProperty.OPACITY,
            "blur" => EffectScriptProperty.BLUR,
            "stroke-width" => EffectScriptProperty.STROKE_WIDTH,
            "letter-spacing" => EffectScriptProperty.LETTER_SPACING,
            "fill-blur" => EffectScriptProperty.FILL_BLUR,
            "stroke-blur" => EffectScriptProperty.STROKE_BLUR,
            "font-size" => EffectScriptProperty.FONT_SIZE,
            "shadow-offset" => EffectScriptProperty.SHADOW_OFFSET,
            "shadow-blur" => EffectScriptProperty.SHADOW_BLUR,
            "shadow-color" => EffectScriptProperty.SHADOW_COLOR,
            "path-progress" => EffectScriptProperty.PATH_PROGRESS,
            "fill" => EffectScriptProperty.FILL,
            "stroke" => EffectScriptProperty.STROKE,
            "mask-rectangle-top-left" => EffectScriptProperty.MASK_RECTANGLE_TOP_LEFT,
            "mask-rectangle-bottom-right" => EffectScriptProperty.MASK_RECTANGLE_BOTTOM_RIGHT,
            "mask-position" => EffectScriptProperty.MASK_POSITION,
            "mask-scale" => EffectScriptProperty.MASK_SCALE,
            "mask-rotation" => EffectScriptProperty.MASK_ROTATION,
            _ => throw new EffectScriptException($"未知动画属性：{text}。", line, column)
        };
        return (property, null);
    }

    internal static AnimationCurve ParseInterpolation(string text, int line, int column)
    {
        var power = powerPattern.Match(text);
        if (power.Success)
        {
            var exponent = ParseNumber(power.Groups["exponent"].Value, line, column);
            if (exponent <= 0)
            {
                throw new EffectScriptException("power 指数必须为有限正数。", line, column);
            }

            return new(KeyframeInterpolation.POWER) { Exponent = exponent };
        }

        return new(text switch
        {
            "" or "linear" => KeyframeInterpolation.LINEAR,
            "hold" => KeyframeInterpolation.HOLD,
            "ease-in" => KeyframeInterpolation.EASE_IN,
            "ease-out" => KeyframeInterpolation.EASE_OUT,
            "ease-in-out" => KeyframeInterpolation.EASE_IN_OUT,
            _ => throw new EffectScriptException($"未知插值方式：{text}。", line, column)
        });
    }

    internal static EffectScriptValue ParseValue(string text, int line, int column)
    {
        if (text == "base")
        {
            return new(EffectScriptValueKind.BASE);
        }

        var kind = text.StartsWith("offset(", StringComparison.Ordinal) ? EffectScriptValueKind.OFFSET :
            text.StartsWith("factor(", StringComparison.Ordinal) ? EffectScriptValueKind.FACTOR : EffectScriptValueKind.ABSOLUTE;
        var opening = text.IndexOf('(', StringComparison.Ordinal);
        var numbers = (opening < 0 ? text : text[(opening + 1)..^1]).Split(',');
        if (text.StartsWith("rgba(", StringComparison.Ordinal))
        {
            return new(EffectScriptValueKind.ABSOLUTE, AnimationValue.FromColor(new(ParseNumber(numbers[0], line, column),
                ParseNumber(numbers[1], line, column), ParseNumber(numbers[2], line, column), ParseNumber(numbers[3], line, column))));
        }

        return new(kind, ParseNumber(numbers[0], line, column), numbers.Length == 2 ? ParseNumber(numbers[1], line, column) : null);
    }

    private static double ParseNumber(string text, int line, int column)
    {
        if (!double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            throw new EffectScriptException("属性值必须为有限十进制数。", line, column);
        }

        return value;
    }

    internal static EffectScriptKeyframe ParseKeyframe(string text, int line, int column)
    {
        var key = keyframePattern.Match(text);
        if (!key.Success)
        {
            throw new EffectScriptException("关键帧应为 at 段内位置 属性 值 [插值方式]；段以 end 结束。", line, column);
        }
        var property = ParseProperty(key.Groups["property"].Value, line, column + key.Groups["property"].Index);
        var curve = ParseInterpolation(key.Groups["interpolation"].Value, line, column + key.Groups["interpolation"].Index);
        return new(ParseDecimal(key.Groups["progress"].Value, line, column + key.Groups["progress"].Index), property.Property,
            ParseValue(key.Groups["value"].Value, line, column + key.Groups["value"].Index), curve.Interpolation, line, column)
        {
            NodeSelector = property.Selector,
            Exponent = curve.Exponent
        };
    }

    private static Regex Pattern(string pattern)
    {
        return new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
