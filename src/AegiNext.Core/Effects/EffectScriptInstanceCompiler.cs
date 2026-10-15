using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal static class EffectScriptInstanceCompiler
{
    internal static CompiledEffectScript Compile(EffectScript script, EffectInstance instance, ProjectLayer layer, SubtitleLine? subtitle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (script.Version != 3)
        {
            throw new EffectScriptException("特效实例只接受版本 3 脚本。");
        }
        EffectScriptV3Validator.Validate(script);
        if (subtitle is not null && layer.SubtitleId != subtitle.Id)
        {
            throw new EffectScriptException("字幕与目标片段不匹配。");
        }
        var (origin, end) = LayerAnimationTiming.GetRange(layer);
        if (instance.Id == Guid.Empty || instance.Start < origin || instance.End > end || instance.End <= instance.Start)
        {
            throw new EffectScriptException("实例时段必须是父片段内容时钟内的正时长区间。");
        }
        if (instance.TargetContext is { } context)
        {
            SubtitleAnimationTargetValidation.ValidateIdentity(context);
            if (context.NodeId.HasValue || context.TextRangeId is { } rangeId && subtitle?.AnimationRanges.All(range => range.Id != rangeId) != false)
            {
                throw new EffectScriptException("实例上下文必须引用当前字幕中存在的文字范围。");
            }
        }
        var symbols = Symbols(script, instance.Parameters, end - origin, instance.End - instance.Start);
        var budget = new EffectScriptExpansionBudget(cancellationToken);
        var ranges = ImmutableArray.CreateBuilder<SubtitleAnimationRange>();
        var channels = ImmutableArray.CreateBuilder<CompiledEffectChannel>();
        foreach (var expanded in Expand(script.Statements, symbols, [], budget))
        {
            var statement = expanded.Statement;
            var scope = Layout(statement, expanded.Symbols);
            var wholeLayer = scope.Unit.Kind == EffectScriptUnitKind.GROUP && scope.Target.Kind != EffectScriptTargetKind.RANGE &&
                (scope.Target.Kind == EffectScriptTargetKind.SUBTITLE || instance.TargetContext?.TextRangeId is null);
            var requiresSubtitle = !wholeLayer || scope.Target.Kind != EffectScriptTargetKind.CURRENT || scope.State != SubtitleAnimationState.NORMAL;
            if (requiresSubtitle && (layer.Kind != LayerKind.SUBTITLE || subtitle is null || layer.SubtitleId != subtitle.Id))
            {
                throw Error(statement, "字幕目标及文字分组需要匹配的字幕片段。");
            }
            var textGroups = wholeLayer ? ImmutableArray<EffectScriptTextGroup>.Empty : EffectScriptGroupResolver.Resolve(scope, subtitle!, instance.TargetContext);
            var count = wholeLayer ? 1 : textGroups.Length;
            var groups = ImmutableArray.CreateBuilder<EffectScriptBoundGroup>();
            for (var index = 0; index < count; index++)
            {
                var local = new Dictionary<string, EffectScriptEvaluationValue>(expanded.Symbols, StringComparer.Ordinal)
                {
                    ["group.index"] = Number(index),
                    ["group.count"] = Number(count)
                };
                var body = Segments(statement.Statements.Where(child => child.Kind != EffectScriptStatementKind.FIELD).ToImmutableArray(), local, budget);
                Guid? id = null;
                if (!wholeLayer)
                {
                    if (scope.Unit.Kind == EffectScriptUnitKind.GROUP && scope.Target.Kind == EffectScriptTargetKind.CURRENT)
                    {
                        id = instance.TargetContext!.Value.TextRangeId;
                    }
                    else
                    {
                        var group = textGroups[index];
                        id = RangeId(instance.Id, statement.Name, expanded.Path, group);
                        if (!body.IsEmpty)
                        {
                            ranges.Add(new(id.Value, group.Utf16Start, group.Utf16Length));
                        }
                    }
                }
                groups.Add(new(id, body));
            }
            if (ranges.Count + (subtitle?.AnimationRanges.Length ?? 0) > 256)
            {
                throw Error(statement, "手工与临时文字范围总数超过 256 个预算。");
            }
            var previousCount = channels.Count;
            Allocate(script.ShortClipPolicy, scope, groups.ToImmutable(), instance, layer, subtitle, ranges.ToImmutable(), channels, cancellationToken);
            for (var index = previousCount; index < channels.Count; index++)
            {
                var added = channels[index];
                if (channels.Take(index).Any(previous => SameTargetComponents(previous.Target, added.Target) && previous.Start < added.End && added.Start < previous.End))
                {
                    throw new EffectScriptException("同一完整目标或旋转分量声明了重叠时间区间。", added.Line, added.Column);
                }
            }
        }
        var targets = channels.Select(channel => channel.Target).Concat(layer.Tracks.Select(track => track.Target))
            .Select(EffectScriptRotationTracks.CanonicalTarget).Distinct().Count(target => target.TextRangeId.HasValue || target.State != SubtitleAnimationState.NORMAL);
        if (targets > 8192)
        {
            throw new EffectScriptException("范围及状态输出超过 8192 条轨道的预算。");
        }
        foreach (var target in channels.GroupBy(channel => channel.Target))
        {
            if (target.Sum(channel => (long)channel.Multiplier.Keyframes.Length) > AnimationPropertyMetadata.GetMaximumTrackEntries(target.Key.Property))
            {
                var first = target.First();
                throw new EffectScriptException("聚合后的系数轨道超过属性关键帧预算。", first.Line, first.Column);
            }
            CompiledEffectChannel? previous = null;
            foreach (var channel in target.OrderBy(channel => channel.Start))
            {
                if (previous is not null && previous.End == channel.Start &&
                    (previous.Multiplier.Keyframes[^1].Value != channel.Multiplier.Keyframes[0].Value || previous.Offset.Keyframes[^1].Value != channel.Offset.Keyframes[0].Value))
                {
                    throw new EffectScriptException("共享端点的仿射值不连续。", channel.Line, channel.Column);
                }
                previous = channel;
            }
        }
        return new(ranges.ToImmutable(), channels.ToImmutable());
    }

    internal static EffectScript ProjectDefaults(EffectScript script)
    {
        try
        {
            var symbols = Symbols(script, null, new(1), new(1));
            var budget = new EffectScriptExpansionBudget(default);
            var scopes = ImmutableArray.CreateBuilder<EffectScriptScope>();
            foreach (var expanded in Expand(script.Statements, symbols, [], budget))
            {
                var scope = Layout(expanded.Statement, expanded.Symbols);
                var local = new Dictionary<string, EffectScriptEvaluationValue>(expanded.Symbols, StringComparer.Ordinal)
                {
                    ["group.index"] = Number(0), ["group.count"] = Number(1)
                };
                var segments = Segments(expanded.Statement.Statements.Where(child => child.Kind != EffectScriptStatementKind.FIELD).ToImmutableArray(), local, budget);
                scopes.Add(scope with { Segments = segments.Select((segment, index) => new EffectScriptSegment("segment-" + index.ToString(CultureInfo.InvariantCulture), segment.FixedDuration,
                    segment.Weight.Decimal, segment.Keys.Select(key => new EffectScriptKeyframe(key.Progress.Decimal, key.Property,
                        ConcreteValue(key), key.Curve.Interpolation, key.Line, key.Column) { NodeSelector = key.NodeSelector, Exponent = key.Curve.Exponent }).ToImmutableArray(), segment.Line, segment.Column)
                    { RepeatCount = segment.RepeatCount, PingPong = segment.PingPong, CycleDuration = segment.CycleDuration }).ToImmutableArray() });
            }
            return script with { Scopes = scopes.ToImmutable() };
        }
        catch (Exception error) when (error is EffectScriptException or OverflowException)
        {
            return script;
        }
    }

    private static EffectScriptValue ConcreteValue(EffectScriptBoundKey key)
    {
        if (All(key.Multiplier, 1) && All(key.Offset, 0))
        {
            return new(EffectScriptValueKind.BASE);
        }
        if (All(key.Multiplier, 1))
        {
            return new(EffectScriptValueKind.OFFSET, key.Offset);
        }
        if (All(key.Multiplier, 0))
        {
            return new(EffectScriptValueKind.ABSOLUTE, key.Offset);
        }
        return new(EffectScriptValueKind.FACTOR, key.Multiplier);
    }

    private static Dictionary<string, EffectScriptEvaluationValue> Symbols(EffectScript script, IReadOnlyDictionary<string, EffectScriptParameterValue>? parameters,
        MediaTime clipDuration, MediaTime effectDuration)
    {
        var symbols = EffectScriptParameterBinder.Bind(script, parameters).ToDictionary(pair => pair.Key, pair => EffectScriptEvaluationValue.From(pair.Value), StringComparer.Ordinal);
        symbols["clip.duration"] = new(EffectScriptParameterKind.DURATION) { Number = EffectScriptRational.From(clipDuration) };
        symbols["effect.duration"] = new(EffectScriptParameterKind.DURATION) { Number = EffectScriptRational.From(effectDuration) };
        return symbols;
    }

    private static IEnumerable<(EffectScriptStatement Statement, Dictionary<string, EffectScriptEvaluationValue> Symbols, ImmutableArray<long> Path)> Expand(
        ImmutableArray<EffectScriptStatement> statements, Dictionary<string, EffectScriptEvaluationValue> symbols, ImmutableArray<long> path, EffectScriptExpansionBudget budget)
    {
        foreach (var statement in statements)
        {
            budget.Step(statement);
            if (statement.Kind == EffectScriptStatementKind.IF)
            {
                var selected = Evaluate(statement.Expressions[0], symbols).Boolean ? statement : statement.Alternatives.FirstOrDefault(alternative =>
                    alternative.Expressions.IsEmpty || Evaluate(alternative.Expressions[0], symbols).Boolean);
                if (selected is not null)
                {
                    foreach (var result in Expand(selected.Statements, symbols, path, budget))
                    {
                        yield return result;
                    }
                }
            }
            else if (statement.Kind == EffectScriptStatementKind.FOR)
            {
                var start = Integer(statement.Expressions[0], symbols);
                var stop = Integer(statement.Expressions[1], symbols);
                var step = statement.Expressions.Length > 2 ? Integer(statement.Expressions[2], symbols) : 1;
                if (step == 0)
                {
                    throw Error(statement, "循环步长不能为零。");
                }
                var count = 0;
                for (var index = new BigInteger(start); step > 0 ? index < stop : index > stop; index += step)
                {
                    budget.Step(statement);
                    if (++count > 4096)
                    {
                        throw Error(statement, "单次循环超过 4096 次预算。");
                    }
                    var local = new Dictionary<string, EffectScriptEvaluationValue>(symbols, StringComparer.Ordinal) { [statement.Name] = Number(checked((long)index)) };
                    foreach (var result in Expand(statement.Statements, local, path.Add(checked((long)index)), budget))
                    {
                        yield return result;
                    }
                }
            }
            else
            {
                yield return (statement, symbols, path);
            }
        }
    }

    private static EffectScriptScope Layout(EffectScriptStatement statement, Dictionary<string, EffectScriptEvaluationValue> symbols)
    {
        var targetExpression = statement.Expressions[0];
        var target = targetExpression.Kind == EffectScriptExpressionKind.CALL
            ? new EffectScriptTargetSelector(EffectScriptTargetKind.RANGE, PositiveInteger(targetExpression.Arguments[0], symbols), PositiveInteger(targetExpression.Arguments[1], symbols))
            : Evaluate(targetExpression, symbols).Text switch
            {
                "current" => new(EffectScriptTargetKind.CURRENT),
                "subtitle" => new(EffectScriptTargetKind.SUBTITLE),
                _ => throw Error(statement, "scope 目标应为 current、subtitle 或 range。")
            };
        var scope = new EffectScriptScope(statement.Name, target, [], statement.Line, statement.Column);
        foreach (var field in statement.Statements.Where(child => child.Kind == EffectScriptStatementKind.FIELD))
        {
            var expression = field.Expressions[0];
            switch (field.Name)
            {
                case "unit":
                    scope = scope with { Unit = Unit(expression, symbols) };
                    break;
                case "delay":
                    scope = scope with { Delay = Duration(expression, symbols, true) };
                    break;
                case "stagger":
                    scope = scope with { Stagger = Duration(expression, symbols, true) };
                    break;
                case "order":
                    scope = scope with { Order = Evaluate(expression, symbols).Text switch { "forward" => EffectScriptOrder.FORWARD, "reverse" => EffectScriptOrder.REVERSE, _ => throw Error(field, "order 应为 forward 或 reverse。") } };
                    break;
                case "state":
                    scope = scope with { State = Evaluate(expression, symbols).Text switch { "normal" => SubtitleAnimationState.NORMAL, "active" => SubtitleAnimationState.ACTIVE, "inactive" => SubtitleAnimationState.INACTIVE, _ => throw Error(field, "state 应为 normal、active 或 inactive。") } };
                    break;
            }
        }
        return scope;
    }

    private static EffectScriptUnit Unit(EffectScriptExpression expression, Dictionary<string, EffectScriptEvaluationValue> symbols)
    {
        if (expression.Kind == EffectScriptExpressionKind.CALL)
        {
            if (expression.Text == "chunk")
            {
                return new(EffectScriptUnitKind.CHUNK) { Count = PositiveInteger(expression.Arguments[0], symbols) };
            }
            var delimiters = expression.Arguments.Select(argument => Evaluate(argument, symbols).Text!).ToImmutableArray();
            if (delimiters.Any(string.IsNullOrEmpty) || delimiters.Distinct(StringComparer.Ordinal).Count() != delimiters.Length)
            {
                throw new EffectScriptException("split 分隔符不能重复或为空。", expression.Line, expression.Column);
            }
            return new(EffectScriptUnitKind.SPLIT) { Delimiters = delimiters };
        }
        return new(Evaluate(expression, symbols).Text switch
        {
            "group" => EffectScriptUnitKind.GROUP, "grapheme" => EffectScriptUnitKind.GRAPHEME, "word" => EffectScriptUnitKind.WORD,
            "line" => EffectScriptUnitKind.LINE, "paragraph" => EffectScriptUnitKind.PARAGRAPH,
            _ => throw new EffectScriptException("未知文字分组方式。", expression.Line, expression.Column)
        });
    }

    private static ImmutableArray<EffectScriptBoundSegment> Segments(ImmutableArray<EffectScriptStatement> statements,
        Dictionary<string, EffectScriptEvaluationValue> symbols, EffectScriptExpansionBudget budget)
    {
        var segments = ImmutableArray.CreateBuilder<EffectScriptBoundSegment>();
        var keyCount = 0;
        foreach (var expanded in Expand(statements, symbols, [], budget))
        {
            var source = expanded.Statement;
            if (segments.Count == 128)
            {
                throw Error(source, "单组展开超过 128 段预算。");
            }
            var fixedDuration = source.Text == "fixed" ? Duration(source.Expressions[0], expanded.Symbols, false) : (MediaTime?)null;
            var weight = fixedDuration.HasValue ? new EffectScriptRational(0, 1) : Evaluate(source.Expressions[0], expanded.Symbols).Number;
            if (!fixedDuration.HasValue && (weight.Compare(new(0, 1)) <= 0 || weight.Compare(new(1000, 1)) > 0))
            {
                throw Error(source, "flex 权重必须大于零且不超过 1000。");
            }
            var repeat = PositiveInteger(source.Expressions[1], expanded.Symbols);
            var cycle = source.HasCycle ? Duration(source.Expressions[2], expanded.Symbols, false) : (MediaTime?)null;
            if (fixedDuration.HasValue && cycle.HasValue || !fixedDuration.HasValue && source.HasRepeat || !fixedDuration.HasValue && source.PingPong && !cycle.HasValue)
            {
                throw Error(source, "repeat 仅用于 fixed，cycle 仅用于 flex；flex pingpong 必须指定 cycle。");
            }
            var keys = ImmutableArray.CreateBuilder<EffectScriptBoundKey>();
            foreach (var expandedKey in Expand(source.Statements, expanded.Symbols, [], budget))
            {
                if (++keyCount > 4096)
                {
                    throw Error(expandedKey.Statement, "单组展开超过 4096 个关键帧预算。");
                }
                keys.Add(Key(expandedKey.Statement, expandedKey.Symbols));
            }
            if (keys.Any(key => key.NativeProperty == AnimationProperty.ROTATION_3D))
            {
                for (var index = 0; index < keys.Count; index++)
                {
                    var key = keys[index];
                    if (key.NativeProperty == AnimationProperty.ROTATION)
                    {
                        if (!All(key.Multiplier, 1) || !All(key.Offset, 0))
                        {
                            throw Error(source, "完整 vec3 旋转不能与标量 rotation 值混用；可使用 rotation-x/y。 ");
                        }
                        keys[index] = key with { NativeProperty = AnimationProperty.ROTATION_3D,
                            Multiplier = AnimationValue.FromVector3(new(1, 1, 1)), Offset = AnimationValue.FromVector3(new(0, 0, 0)) };
                    }
                }
            }
            ValidateKeys(keys, source);
            segments.Add(new(source.Name, fixedDuration, weight, repeat, source.PingPong, cycle, keys.ToImmutable(), source.Line, source.Column));
        }
        if (segments.Count > 0 && (!segments.Any(segment => !segment.FixedDuration.HasValue) || !segments.Any(segment => !segment.Keys.IsEmpty)))
        {
            var first = statements[0];
            throw Error(first, "每组展开结果必须有 flex 段和关键帧。");
        }
        return segments.ToImmutable();
    }

    private static EffectScriptBoundKey Key(EffectScriptStatement source, Dictionary<string, EffectScriptEvaluationValue> symbols)
    {
        var progress = Evaluate(source.Expressions[0], symbols).Number;
        if (progress.Compare(new(0, 1)) < 0 || progress.Compare(new(1, 1)) > 0)
        {
            throw Error(source, "at 位置必须在 0 到 1 之间。");
        }
        var parsed = EffectScriptParser.ParseProperty(source.Text, source.Line, source.Column);
        var property = EffectScriptPropertyMetadata.GetAnimationProperty(parsed.Property);
        var literal = Evaluate(source.Expressions[1], symbols);
        if (parsed.Property == EffectScriptProperty.ROTATION && source.ValueKind != EffectScriptValueKind.BASE && literal.Kind == EffectScriptParameterKind.VECTOR3)
        {
            property = AnimationProperty.ROTATION_3D;
        }
        var kind = AnimationPropertyMetadata.GetValueKind(property);
        var value = literal.Animation ?? AnimationValue.FromScalar(literal.Number.Double);
        var zero = Zero(kind);
        var one = Fill(zero, 1);
        var multiplier = source.ValueKind switch { EffectScriptValueKind.BASE or EffectScriptValueKind.OFFSET => one, EffectScriptValueKind.FACTOR => value, _ => zero };
        var offset = source.ValueKind is EffectScriptValueKind.ABSOLUTE or EffectScriptValueKind.OFFSET ? value : zero;
        if (multiplier.Kind != kind || offset.Kind != kind)
        {
            throw Error(source, "关键帧维度与属性不匹配。");
        }
        if (source.ValueKind == EffectScriptValueKind.ABSOLUTE && Enumerable.Range(0, value.ComponentCount).Any(index =>
            !double.IsFinite(value.GetComponent(index)) || value.GetComponent(index) < AnimationPropertyMetadata.GetMinimum(property, index) ||
            value.GetComponent(index) > AnimationPropertyMetadata.GetMaximum(property, index)))
        {
            throw Error(source, "绝对属性值超出项目允许范围。");
        }
        var exponent = Evaluate(source.Expressions[2], symbols).Number.Double;
        if (!double.IsFinite(exponent) || exponent <= 0)
        {
            throw Error(source, "power 指数必须为有限正数。");
        }
        var curve = new AnimationCurve(source.Name switch
        {
            "hold" => KeyframeInterpolation.HOLD, "ease-in" => KeyframeInterpolation.EASE_IN,
            "ease-out" => KeyframeInterpolation.EASE_OUT, "ease-in-out" => KeyframeInterpolation.EASE_IN_OUT,
            "power" => KeyframeInterpolation.POWER, _ => KeyframeInterpolation.LINEAR
        }) { Exponent = exponent };
        return new(progress, parsed.Property, property, parsed.Selector, multiplier, offset, curve, source.Line, source.Column);
    }

    private static void ValidateKeys(ImmutableArray<EffectScriptBoundKey>.Builder keys, EffectScriptStatement source)
    {
        foreach (var flow in keys.GroupBy(key => (key.NativeProperty, key.NodeSelector)))
        {
            var previous = new EffectScriptRational(-1, 1);
            foreach (var key in flow)
            {
                if (previous.Compare(new(-1, 1)) == 0 ? !key.Progress.IsZero : key.Progress.Compare(previous) <= 0)
                {
                    throw Error(source, "每个属性必须从 at 0 开始，位置严格递增。");
                }
                previous = key.Progress;
            }
            if (previous.Compare(new(1, 1)) != 0)
            {
                throw Error(source, "每个属性必须以 at 1 结束。");
            }
        }
    }

    private static void Allocate(EffectScriptShortClipPolicy policy, EffectScriptScope scope, ImmutableArray<EffectScriptBoundGroup> groups,
        EffectInstance instance, ProjectLayer layer, SubtitleLine? subtitle, ImmutableArray<SubtitleAnimationRange> ranges,
        ImmutableArray<CompiledEffectChannel>.Builder channels, CancellationToken cancellationToken)
    {
        if (groups.IsEmpty || groups.All(group => group.Segments.IsEmpty))
        {
            return;
        }
        var fixedTotals = groups.Select(group => group.Segments.Aggregate(MediaTime.Zero, (sum, segment) => sum +
            (segment.FixedDuration is { } fixedTime ? fixedTime * segment.RepeatCount * (segment.PingPong ? 2 : 1) : MediaTime.Zero))).ToArray();
        var weights = groups.Select(group => group.Segments.Aggregate(new EffectScriptRational(0, 1), (sum, segment) => EffectScriptRational.Add(sum, segment.Weight))).ToArray();
        var maximum = MediaTime.Zero;
        for (var index = 0; index < groups.Length; index++)
        {
            var minimum = MediaTime.Zero;
            foreach (var segment in groups[index].Segments.Where(segment => segment.CycleDuration.HasValue))
            {
                var required = Scale(segment.CycleDuration!.Value, EffectScriptRational.Divide(weights[index], segment.Weight));
                if (required > minimum)
                {
                    minimum = required;
                }
            }
            if (fixedTotals[index] + minimum > maximum)
            {
                maximum = fixedTotals[index] + minimum;
            }
        }
        var staggerSpan = scope.Stagger * (groups.Length - 1);
        var envelope = scope.Delay + staggerSpan + maximum;
        var duration = instance.End - instance.Start;
        if (duration < envelope && policy == EffectScriptShortClipPolicy.REJECT)
        {
            throw new EffectScriptException("实例时长小于作用范围所需包络。", scope.Line, scope.Column);
        }
        var compression = duration < envelope ? EffectScriptRational.Divide(EffectScriptRational.From(duration), EffectScriptRational.From(envelope)) : new EffectScriptRational(1, 1);
        var window = duration - Scale(scope.Delay + staggerSpan, compression);
        var projectedSubtitle = subtitle is null ? null : subtitle with { AnimationRanges = subtitle.AnimationRanges.AddRange(ranges) };
        for (var index = 0; index < groups.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = groups[index];
            var cursor = instance.Start + Scale(scope.Delay + scope.Stagger * index, compression);
            var remaining = window - Scale(fixedTotals[index], compression);
            foreach (var segment in group.Segments)
            {
                var legs = segment.PingPong ? 2 : 1;
                var length = segment.FixedDuration is { } fixedTime ? Scale(fixedTime * segment.RepeatCount * legs, compression) :
                    Scale(remaining, EffectScriptRational.Divide(segment.Weight, weights[index]));
                var period = segment.FixedDuration is { } fixedLeg ? Scale(fixedLeg * legs, compression) : segment.CycleDuration is { } cycle ? Scale(cycle, compression) : length;
                var cycles = segment.FixedDuration.HasValue ? new BigInteger(segment.RepeatCount) : segment.CycleDuration.HasValue ? EffectScriptTiming.ExactFloor(length, period) : BigInteger.One;
                var leg = period == MediaTime.Zero ? MediaTime.Zero : period / legs;
                foreach (var flow in segment.Keys.GroupBy(key => (key.NativeProperty, key.NodeSelector)))
                {
                    var frames = flow.ToImmutableArray();
                    var first = frames[0];
                    var target = Resolve(first, layer, projectedSubtitle, group.RangeId, scope.State);
                    var count = BigInteger.One + cycles * legs * (frames.Length - 1);
                    if (count > AnimationPropertyMetadata.GetMaximumTrackEntries(target.Property))
                    {
                        throw new EffectScriptException("重复展开后的关键帧超过属性预算。", first.Line, first.Column);
                    }
                    if (period * checked((long)cycles) < length && ++count > AnimationPropertyMetadata.GetMaximumTrackEntries(target.Property))
                    {
                        throw new EffectScriptException("循环剩余保持点超过属性预算。", first.Line, first.Column);
                    }
                    if ((segment.RepeatCount > 1 || segment.CycleDuration.HasValue) && !segment.PingPong && !Same(frames[0], frames[^1]))
                    {
                        throw new EffectScriptException("正向重复和循环的仿射端点必须闭合。", first.Line, first.Column);
                    }
                    var multiplier = ImmutableArray.CreateBuilder<Keyframe>();
                    var offset = ImmutableArray.CreateBuilder<Keyframe>();
                    if (leg == MediaTime.Zero)
                    {
                        if (frames.Any(frame => !Same(first, frame)))
                        {
                            throw new EffectScriptException("零时长段的仿射值不能变化。", first.Line, first.Column);
                        }
                        multiplier.Add(Frame(cursor, first.Multiplier, first.Curve));
                        offset.Add(Frame(cursor, first.Offset, first.Curve));
                    }
                    else
                    {
                        for (var repeat = 0; repeat < (int)cycles; repeat++)
                        {
                            for (var direction = 0; direction < legs; direction++)
                            {
                                for (var point = 0; point < frames.Length; point++)
                                {
                                    var sourceIndex = direction == 0 ? point : frames.Length - 1 - point;
                                    var key = frames[sourceIndex];
                                    var progress = direction == 0 ? key.Progress : EffectScriptRational.Subtract(new(1, 1), key.Progress);
                                    var curve = direction == 0 ? key.Curve : sourceIndex > 0 ? frames[sourceIndex - 1].Curve with { Reverse = !frames[sourceIndex - 1].Curve.Reverse } : new(KeyframeInterpolation.HOLD);
                                    var time = cursor + leg * (repeat * legs + direction) + Scale(leg, progress);
                                    Add(multiplier, Frame(time, key.Multiplier, curve), key);
                                    Add(offset, Frame(time, key.Offset, curve), key);
                                }
                            }
                        }
                    }
                    Hold(multiplier, cursor + length);
                    Hold(offset, cursor + length);
                    channels.Add(new(new(target, multiplier.ToImmutable()), new(target, offset.ToImmutable()), cursor, cursor + length, first.Line, first.Column));
                }
                cursor += length;
            }
        }
    }

    private static bool SameTargetComponents(AnimationTrackTarget first, AnimationTrackTarget second)
    {
        if (EffectScriptRotationTracks.CanonicalTarget(first) != EffectScriptRotationTracks.CanonicalTarget(second))
        {
            return false;
        }
        static int Mask(AnimationProperty property) => property switch
        {
            AnimationProperty.ROTATION_X => 1,
            AnimationProperty.ROTATION_Y => 2,
            AnimationProperty.ROTATION => 4,
            _ => 7
        };
        return (Mask(first.Property) & Mask(second.Property)) != 0;
    }

    private static AnimationTrackTarget Resolve(EffectScriptBoundKey key, ProjectLayer layer, SubtitleLine? subtitle, Guid? rangeId, SubtitleAnimationState state)
    {
        if (AnimationPropertyMetadata.IsMaskProperty(key.NativeProperty) && layer.Mask is null)
        {
            throw new EffectScriptException("目标片段尚无蒙版；脚本只能动画已有几何。", key.Line, key.Column);
        }
        if (key.NativeProperty == AnimationProperty.PATH_PROGRESS && layer.MotionPath is null)
        {
            throw new EffectScriptException("路径动画需要已有运动路径。", key.Line, key.Column);
        }
        if (AnimationPropertyMetadata.IsSubtitleOnlyProperty(key.NativeProperty) && subtitle is null)
        {
            throw new EffectScriptException("字幕排版和绘制属性需要原字幕样式。", key.Line, key.Column);
        }
        var frame = new EffectScriptKeyframe(0, key.Property, new(EffectScriptValueKind.BASE), Line: key.Line, Column: key.Column) { NodeSelector = key.NodeSelector };
        var target = EffectScriptCompiler.ResolveInstanceTarget(frame, layer, new(AnimationProperty.FILL, TextRangeId: rangeId, State: state), subtitle) with { Property = key.NativeProperty };
        if (rangeId.HasValue && !AnimationPropertyMetadata.IsTextRangeProperty(target.Property) || state != SubtitleAnimationState.NORMAL && !AnimationPropertyMetadata.IsSubtitleVisualProperty(target.Property))
        {
            throw new EffectScriptException("当前范围或视觉状态不支持此属性。", key.Line, key.Column);
        }
        return target;
    }

    private static void Add(ImmutableArray<Keyframe>.Builder frames, Keyframe frame, EffectScriptBoundKey source)
    {
        if (frames.Count > 0 && frames[^1].Time == frame.Time)
        {
            if (frames[^1].Value != frame.Value)
            {
                throw new EffectScriptException("共享端点的仿射值不连续。", source.Line, source.Column);
            }
            frames[^1] = frame;
        }
        else
        {
            frames.Add(frame);
        }
    }

    private static void Hold(ImmutableArray<Keyframe>.Builder frames, MediaTime end)
    {
        if (frames.Count > 0 && frames[^1].Time < end)
        {
            frames[^1] = frames[^1] with { Interpolation = KeyframeInterpolation.HOLD, Reverse = false };
            frames.Add(new(end, frames[^1].Value, KeyframeInterpolation.HOLD));
        }
    }

    private static Keyframe Frame(MediaTime time, AnimationValue value, AnimationCurve curve) => new(time, value, curve.Interpolation)
    { Exponent = curve.Exponent, CurveStart = curve.CurveStart, CurveEnd = curve.CurveEnd, Reverse = curve.Reverse };
    private static bool Same(EffectScriptBoundKey first, EffectScriptBoundKey second) => first.Multiplier == second.Multiplier && first.Offset == second.Offset;
    private static bool All(AnimationValue value, double expected) => Enumerable.Range(0, value.ComponentCount).All(index => value.GetComponent(index).Equals(expected));
    private static AnimationValue Zero(AnimationValueKind kind) => kind switch
    {
        AnimationValueKind.VECTOR => AnimationValue.FromVector(new(0, 0)), AnimationValueKind.VECTOR3 => AnimationValue.FromVector3(new(0, 0, 0)),
        AnimationValueKind.COLOR => AnimationValue.FromColor(new(0, 0, 0, 0)), _ => AnimationValue.FromScalar(0)
    };
    private static AnimationValue Fill(AnimationValue value, double number)
    {
        for (var index = 0; index < value.ComponentCount; index++)
        {
            value = value.WithComponent(index, number);
        }
        return value;
    }
    private static MediaTime Scale(MediaTime time, EffectScriptRational factor) => EffectScriptRational.Multiply(EffectScriptRational.From(time), factor).Time;
    private static EffectScriptEvaluationValue Number(long value) => new(EffectScriptParameterKind.NUMBER) { Number = new(value, 1) };
    private static EffectScriptEvaluationValue Evaluate(EffectScriptExpression expression, Dictionary<string, EffectScriptEvaluationValue> symbols) => EffectScriptExpressionEvaluator.Evaluate(expression, symbols);
    private static long Integer(EffectScriptExpression expression, Dictionary<string, EffectScriptEvaluationValue> symbols)
    {
        try
        {
            return Evaluate(expression, symbols).Number.Integer;
        }
        catch (Exception error) when (error is OverflowException or InvalidOperationException)
        {
            throw new EffectScriptException("表达式需要可表示的整数。", expression.Line, expression.Column, error);
        }
    }

    private static int PositiveInteger(EffectScriptExpression expression, Dictionary<string, EffectScriptEvaluationValue> symbols)
    {
        var number = Integer(expression, symbols);
        if (number is < 1 or > int.MaxValue)
        {
            throw new EffectScriptException("需要可表示的正整数。", expression.Line, expression.Column);
        }
        return (int)number;
    }
    private static MediaTime Duration(EffectScriptExpression expression, Dictionary<string, EffectScriptEvaluationValue> symbols, bool allowZero)
    {
        var time = Evaluate(expression, symbols).Number.Time;
        if (time < MediaTime.Zero || !allowZero && time == MediaTime.Zero || time > new MediaTime(86400))
        {
            throw new EffectScriptException("时长须为正且不超过 24 小时；delay/stagger 允许零。", expression.Line, expression.Column);
        }
        return time;
    }
    private static Guid RangeId(Guid instance, string scope, ImmutableArray<long> path, EffectScriptTextGroup group)
    {
        var identity = instance.ToString("N", CultureInfo.InvariantCulture) + ":" + scope + ":" + string.Join(',', path.Select(value => value.ToString(CultureInfo.InvariantCulture))) +
            ":" + group.Utf16Start.ToString(CultureInfo.InvariantCulture) + ":" + group.Utf16Length.ToString(CultureInfo.InvariantCulture);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16), true);
    }
    private static EffectScriptException Error(EffectScriptStatement statement, string message) => new(message, statement.Line, statement.Column);
}
