using System.Collections.Immutable;
using AegiNext.Core.Timing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>验证脚本数据接口和文本解析共享的结构、维度与资源预算。</summary>
public static class EffectScriptValidator
{
    public const int MAXIMUM_SEGMENTS = 128;
    public const int MAXIMUM_KEYFRAMES = 4096;
    public const int MAXIMUM_FIXED_SECONDS = 86400;
    public const int MAXIMUM_SCOPES = 128;

    /// <summary>验证所有时间段；不解析目标片段的基础值，不修改输入。</summary>
    public static void Validate(EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Version == 3)
        {
            EffectScriptV3Validator.Validate(script);
            return;
        }
        if (!IsIdentifier(script.Id) || !Enum.IsDefined(script.ShortClipPolicy) || script.Version is not (1 or 2) ||
            script.Segments.IsDefault || script.Scopes.IsDefault)
        {
            throw new EffectScriptException("脚本标识、版本、短片段规则或集合无效。");
        }
        var totalSegments = 0;
        var totalKeys = 0;
        if (script.Version == 1)
        {
            if (!script.Scopes.IsEmpty)
            {
                throw new EffectScriptException("版本 1 不能包含作用范围。");
            }
            ValidateSegments(script.Segments, false, ref totalSegments, ref totalKeys);
            return;
        }
        if (!script.Segments.IsEmpty || script.Scopes.IsEmpty || script.Scopes.Length > MAXIMUM_SCOPES)
        {
            throw new EffectScriptException("版本 2 必须包含有界命名作用范围，不能同时包含旧版顶层时间段。");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in script.Scopes)
        {
            if (scope is null)
            {
                throw new EffectScriptException("作用范围不能为空。");
            }
            if (!IsIdentifier(scope.Name) || !names.Add(scope.Name) || scope.Target is null || scope.Unit is null ||
                !Enum.IsDefined(scope.Order) || !Enum.IsDefined(scope.State) ||
                scope.Delay < MediaTime.Zero || scope.Delay > new MediaTime(MAXIMUM_FIXED_SECONDS) ||
                scope.Stagger < MediaTime.Zero || scope.Stagger > new MediaTime(MAXIMUM_FIXED_SECONDS))
            {
                throw new EffectScriptException("作用范围名称、目标、分组、顺序、状态或时序无效。", scope.Line, scope.Column);
            }
            ValidateTarget(scope.Target, scope.Line, scope.Column);
            ValidateUnit(scope.Unit, scope.Line, scope.Column);
            ValidateSegments(scope.Segments, true, ref totalSegments, ref totalKeys, scope.Line, scope.Column);
        }
    }

    private static void ValidateSegments(ImmutableArray<EffectScriptSegment> segments, bool versionTwo,
        ref int totalSegments, ref int totalKeys, int line = 0, int column = 1)
    {
        if (segments.IsDefaultOrEmpty || segments.Length > MAXIMUM_SEGMENTS ||
            totalSegments > MAXIMUM_SEGMENTS - segments.Length)
        {
            throw new EffectScriptException("时间段集合无效或超过脚本聚合预算。", line, column);
        }
        totalSegments += segments.Length;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var hasFlex = false;
        var localKeys = 0;
        foreach (var segment in segments)
        {
            if (segment is null)
            {
                throw new EffectScriptException("时间段不能为空。");
            }

            if (!IsIdentifier(segment.Name) || !names.Add(segment.Name) || segment.Keyframes.IsDefault)
            {
                throw new EffectScriptException("时间段名称无效、重复或关键帧集合缺失。", segment.Line, segment.Column);
            }

            if (segment.RepeatCount <= 0 ||
                !versionTwo && (segment.RepeatCount != 1 || segment.PingPong || segment.CycleDuration.HasValue) ||
                segment.FixedDuration.HasValue && segment.CycleDuration.HasValue ||
                !segment.FixedDuration.HasValue && (segment.RepeatCount != 1 || segment.PingPong && !segment.CycleDuration.HasValue) ||
                segment.CycleDuration is { } cycle && (cycle <= MediaTime.Zero || cycle > new MediaTime(MAXIMUM_FIXED_SECONDS)))
            {
                throw new EffectScriptException("段重复及循环无效；repeat 仅用于版本 2 固定段，cycle 仅用于版本 2 自由段。",
                    segment.Line, segment.Column);
            }

            if (segment.FixedDuration is { } duration)
            {
                if (duration <= MediaTime.Zero || duration > new MediaTime(MAXIMUM_FIXED_SECONDS) || segment.FlexWeight != 0)
                {
                    throw new EffectScriptException("固定段时长必须大于零且不超过 24 小时，不能同时设置自由权重。", segment.Line, segment.Column);
                }
            }
            else
            {
                hasFlex = true;
                if (segment.FlexWeight <= 0 || segment.FlexWeight > 1000 || decimal.Round(segment.FlexWeight, 6) != segment.FlexWeight)
                {
                    throw new EffectScriptException("自由段权重必须大于零、不超过 1000，且最多六位小数。", segment.Line, segment.Column);
                }
            }

            if (segment.Keyframes.Length > MAXIMUM_KEYFRAMES || totalKeys > MAXIMUM_KEYFRAMES - segment.Keyframes.Length)
            {
                throw new EffectScriptException("关键帧数量超过预算。", segment.Line, segment.Column);
            }
            totalKeys += segment.Keyframes.Length;
            localKeys += segment.Keyframes.Length;

            var previous = new Dictionary<(EffectScriptProperty Property, EffectScriptNodeSelector? Selector), decimal>();
            foreach (var frame in segment.Keyframes)
            {
                if (frame is null || frame.Value is null)
                {
                    throw new EffectScriptException("关键帧或属性值不能为空。", segment.Line, segment.Column);
                }

                if (!Enum.IsDefined(frame.Property) || !Enum.IsDefined(frame.Interpolation) || frame.Progress is < 0 or > 1 ||
                    decimal.Round(frame.Progress, 6) != frame.Progress || !double.IsFinite(frame.Exponent) || frame.Exponent <= 0)
                {
                    throw new EffectScriptException("关键帧属性或插值无效，段内位置应为 0 到 1，最多六位小数。", frame.Line, frame.Column);
                }

                var animationProperty = EffectScriptPropertyMetadata.GetAnimationProperty(frame.Property);
                if (AnimationPropertyMetadata.IsNodeProperty(animationProperty) != frame.NodeSelector.HasValue ||
                    frame.NodeSelector is { } selector &&
                    (selector.ContourNumber is < 1 or > 10000 || selector.NodeNumber is < 1 or > 10000))
                {
                    throw new EffectScriptException("只有蒙版节点属性携带一基轮廓及节点序号。", frame.Line, frame.Column);
                }

                var target = (frame.Property, frame.NodeSelector);
                if (previous.TryGetValue(target, out var last) ? frame.Progress <= last : frame.Progress != 0)
                {
                    throw new EffectScriptException("段内同一属性必须从 at 0 开始且位置严格递增。", frame.Line, frame.Column);
                }

                previous[target] = frame.Progress;
                var value = frame.Value;
                var expectedKind = AnimationPropertyMetadata.GetValueKind(animationProperty);
                if (!Enum.IsDefined(value.Kind) || (value.Kind == EffectScriptValueKind.BASE ? value.Literal.HasValue :
                    value.Literal is not { } literal || literal.Kind != expectedKind ||
                    Enumerable.Range(0, literal.ComponentCount).Any(component => !double.IsFinite(literal.GetComponent(component)))))
                {
                    throw new EffectScriptException("属性值维度不匹配或数值无效；位置、缩放和阴影偏移需要二维向量，颜色需要完整线性 rgba，base 不带参数。",
                        frame.Line, frame.Column);
                }

                if (expectedKind == AnimationValueKind.COLOR)
                {
                    if (value.Kind is not (EffectScriptValueKind.ABSOLUTE or EffectScriptValueKind.BASE))
                    {
                        throw new EffectScriptException("颜色属性仅支持 rgba(r,g,b,a) 或 base，不支持 offset/factor。", frame.Line, frame.Column);
                    }

                    if (value.Literal is { } colorValue && Enumerable.Range(0, 4).Any(component =>
                        colorValue.GetComponent(component) < (component == 3 ? 0 : -65504) ||
                        colorValue.GetComponent(component) > (component == 3 ? 1 : 65504)))
                    {
                        throw new EffectScriptException("线性 rgba 的 RGB 必须在 ±65504 内，Alpha 必须在 0 到 1 内。", frame.Line, frame.Column);
                    }
                }

                if (frame.Property == EffectScriptProperty.PATH_PROGRESS && value.Kind != EffectScriptValueKind.ABSOLUTE)
                {
                    throw new EffectScriptException("path-progress 需要显式的 0 到 1 数值，没有固定基础值。", frame.Line, frame.Column);
                }
            }

            if (previous.Values.Any(progress => progress != 1))
            {
                throw new EffectScriptException("段内每个属性都必须以 at 1 结束。", segment.Line, segment.Column);
            }
        }

        if (!hasFlex || localKeys == 0)
        {
            throw new EffectScriptException("脚本或作用范围必须至少包含一个自由段和一个有关键帧的时间段。", line, column);
        }
    }

    private static void ValidateTarget(EffectScriptTargetSelector target, int line, int column)
    {
        if (!Enum.IsDefined(target.Kind) || target.Start <= 0 || target.Count <= 0 ||
            (long)target.Start + target.Count - 1 > int.MaxValue ||
            target.Kind != EffectScriptTargetKind.RANGE && (target.Start != 1 || target.Count != 1))
        {
            throw new EffectScriptException("作用范围目标无效；固定区间使用一基字素起点和正字素数量。", line, column);
        }
    }

    private static void ValidateUnit(EffectScriptUnit unit, int line, int column)
    {
        if (!Enum.IsDefined(unit.Kind) || unit.Count <= 0 || unit.Delimiters.IsDefault ||
            unit.Kind != EffectScriptUnitKind.CHUNK && unit.Count != 1 ||
            (unit.Kind == EffectScriptUnitKind.SPLIT ? unit.Delimiters.IsEmpty : !unit.Delimiters.IsEmpty))
        {
            throw new EffectScriptException("分组方式、分块数量或文字分隔符集合无效。", line, column);
        }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var delimiter in unit.Delimiters)
        {
            if (string.IsNullOrEmpty(delimiter) || !unique.Add(delimiter))
            {
                throw new EffectScriptException("文字分隔符不能为空或重复。", line, column);
            }
            try
            {
                ProjectValidator.ValidateText(delimiter);
            }
            catch (InvalidDataException error)
            {
                throw new EffectScriptException("文字分隔符包含无效 Unicode。", line, column, error);
            }
        }
    }

    private static bool IsIdentifier(string? value)
    {
        return value is { Length: > 0 and <= 64 } && char.IsAsciiLetterLower(value[0]) &&
            value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');
    }
}
