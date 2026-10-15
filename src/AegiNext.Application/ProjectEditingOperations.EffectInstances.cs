using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>为每个目标片段添加源码独立、覆盖完整内容时段的实例；任一目标失败拒绝整批。</summary>
    public static ProjectDocument AddEffectInstances(ProjectDocument document, IReadOnlyCollection<Guid> layerIds,
        string name, string source, ImmutableDictionary<string, EffectScriptParameterValue>? parameters = null,
        AnimationTrackTarget? targetContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(document);
        var selection = layerIds.ToHashSet();
        if (targetContext?.TextRangeId.HasValue == true && selection.Count != 1)
        {
            throw new EffectScriptException("文字范围特效实例必须属于一个字幕片段。");
        }
        if (selection.Count == 0)
        {
            return document;
        }
        var script = EffectScriptParser.Parse(source);
        var bound = EffectScriptParameterBinder.Bind(script, parameters);
        var layers = MapTrackLayers(document.Layers, layer =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!selection.Remove(layer.Id))
            {
                return layer;
            }
            var (start, end) = LayerAnimationTiming.GetRange(layer);
            var instance = new EffectInstance
            {
                Name = name, Source = source, Parameters = bound, Start = start, End = end,
                TargetContext = targetContext
            };
            return layer with
            {
                Effects = layer.Effects.Add(instance),
                EffectRowOrder = layer.EffectRowOrder.IsEmpty ? [] : layer.EffectRowOrder.Insert(0, new(InstanceId: instance.Id))
            };
        });
        if (selection.Count != 0)
        {
            throw new KeyNotFoundException("图层不存在。");
        }
        return VerifyEffectInstanceEdit(document with { Layers = layers }, document, cancellationToken);
    }

    /// <summary>修改一个实例的固定参数，保留身份、源码及内容时段。</summary>
    public static ProjectDocument UpdateEffectInstance(ProjectDocument document, Guid layerId, Guid instanceId,
        ImmutableDictionary<string, EffectScriptParameterValue> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return UpdateEffectInstances(document, [new(layerId, instanceId)], instance => instance with
        {
            Parameters = EffectScriptParameterBinder.Bind(EffectScriptParser.Parse(instance.Source), parameters)
        }, cancellationToken);
    }

    /// <summary>在一次不可变事务中更新明确指定的实例；身份不能改变，未知目标整批拒绝。</summary>
    public static ProjectDocument UpdateEffectInstances(ProjectDocument document, IReadOnlyCollection<EffectInstanceId> instances,
        Func<EffectInstance, EffectInstance> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return EditEffectInstances(document, instances, instance =>
        {
            var next = update(instance) ?? throw new InvalidOperationException("特效更新不能返回 null。");
            if (next.Id != instance.Id)
            {
                throw new InvalidOperationException("更新特效实例不能改变稳定标识。");
            }
            ArgumentNullException.ThrowIfNull(next.Parameters);
            return instance.Parameters.Count == next.Parameters.Count &&
                instance.Parameters.All(pair => next.Parameters.TryGetValue(pair.Key, out var value) && pair.Value == value) &&
                (next with { Parameters = instance.Parameters }) == instance ? instance : next;
        }, cancellationToken);
    }

    /// <summary>设置实例在片段内容时钟中的正长度时段；超出父片段的请求整体拒绝。</summary>
    public static ProjectDocument SetEffectInstanceTiming(ProjectDocument document, Guid layerId, Guid instanceId,
        MediaTime start, MediaTime end, CancellationToken cancellationToken = default)
    {
        return UpdateEffectInstances(document, [new(layerId, instanceId)], instance => instance with
        {
            Start = start, End = end
        }, cancellationToken);
    }

    /// <summary>设置实例启用状态，禁用保留源码、参数和栈位置。</summary>
    public static ProjectDocument SetEffectInstanceEnabled(ProjectDocument document, Guid layerId, Guid instanceId,
        bool isEnabled, CancellationToken cancellationToken = default)
    {
        return SetEffectInstanceEnabled(document, [new(layerId, instanceId)], isEnabled, cancellationToken);
    }

    /// <summary>原子启用或禁用一批实例，不改变任何派生轨道或手建范围。</summary>
    public static ProjectDocument SetEffectInstanceEnabled(ProjectDocument document, IReadOnlyCollection<EffectInstanceId> instances,
        bool isEnabled, CancellationToken cancellationToken = default)
    {
        return UpdateEffectInstances(document, instances, instance => instance with { IsEnabled = isEnabled }, cancellationToken);
    }

    /// <summary>将实例移动到所属片段内指定的零基栈位置，其他实例相对顺序保持不变。</summary>
    public static ProjectDocument MoveEffectInstance(ProjectDocument document, Guid layerId, Guid instanceId,
        int newIndex, CancellationToken cancellationToken = default)
    {
        ProjectValidator.Validate(document);
        cancellationToken.ThrowIfCancellationRequested();
        var layer = document.Layers.FirstOrDefault(value => value.Id == layerId) ?? throw new KeyNotFoundException("图层不存在。");
        var oldIndex = FindEffectInstance(layer, instanceId);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, layer.Effects.Length);
        if (oldIndex == newIndex)
        {
            return document;
        }
        var effects = layer.Effects.RemoveAt(oldIndex).Insert(newIndex, layer.Effects[oldIndex]);
        var subtitle = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        var next = layer with { Effects = effects, EffectRowOrder = ReplaceInstanceRowOrder(layer, subtitle, effects) };
        return VerifyEffectInstanceEdit(document with { Layers = document.Layers.Replace(layer, next) }, document, cancellationToken);
    }

    /// <summary>只删除指定特效实例，保留父片段的基础属性与原生动画。</summary>
    public static ProjectDocument RemoveEffectInstance(ProjectDocument document, Guid layerId, Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        return RemoveEffectInstances(document, [new(layerId, instanceId)], cancellationToken);
    }

    /// <summary>原子删除多个实例；未知片段或实例身份使整批保持原状。</summary>
    public static ProjectDocument RemoveEffectInstances(ProjectDocument document, IReadOnlyCollection<EffectInstanceId> instances,
        CancellationToken cancellationToken = default)
    {
        return EditEffectInstances(document, instances, _ => null, cancellationToken);
    }

    internal static void ValidateEffectInstanceCompilation(ProjectDocument document, ProjectDocument? previous = null,
        CancellationToken cancellationToken = default)
    {
        var beforeLayers = previous?.Layers.ToDictionary(layer => layer.Id);
        var beforeSubtitles = previous?.Subtitles.ToDictionary(line => line.Id);
        var subtitles = document.Subtitles.ToDictionary(line => line.Id);
        foreach (var layer in document.Layers.Where(layer => !layer.Effects.IsEmpty))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subtitle = layer.SubtitleId is { } id ? subtitles[id] : null;
            if (beforeLayers?.GetValueOrDefault(layer.Id) == layer &&
                (subtitle is null || beforeSubtitles?.GetValueOrDefault(subtitle.Id) == subtitle))
            {
                continue;
            }
            var rangeCount = subtitle?.AnimationRanges.Length ?? 0;
            var targets = layer.Tracks.Select(track => track.Target).ToHashSet();
            foreach (var instance in layer.Effects.Where(instance => instance.IsEnabled))
            {
                var compilation = EffectScriptCompiler.CompileInstance(EffectScriptParser.Parse(instance.Source), instance, layer, subtitle,
                    cancellationToken);
                rangeCount += compilation.Ranges.Length;
                targets.UnionWith(compilation.Channels.Select(channel => channel.Target));
                if (rangeCount > 256 || targets.Count(target => target.TextRangeId.HasValue ||
                        target.State != SubtitleAnimationState.NORMAL) > 8192)
                {
                    throw new EffectScriptException("特效实例栈超出文字范围或动画目标预算。");
                }
            }
        }
    }

    private static ProjectDocument EditEffectInstances(ProjectDocument document, IReadOnlyCollection<EffectInstanceId> instances,
        Func<EffectInstance, EffectInstance?> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(document);
        var remaining = instances.ToHashSet();
        if (remaining.Any(id => id is null || id.LayerId == Guid.Empty || id.InstanceId == Guid.Empty))
        {
            throw new ArgumentException("特效实例目标标识无效。", nameof(instances));
        }
        if (remaining.Count == 0)
        {
            return document;
        }
        var layers = MapTrackLayers(document.Layers, layer =>
        {
            var effects = layer.Effects.Select(instance =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return remaining.Remove(new(layer.Id, instance.Id)) ? update(instance) : instance;
            }).Where(instance => instance is not null).Select(instance => instance!).ToImmutableArray();
            return effects.SequenceEqual(layer.Effects) ? layer : layer with { Effects = effects };
        });
        if (remaining.Count != 0)
        {
            throw new KeyNotFoundException("特效实例不存在。");
        }
        return layers == document.Layers ? document :
            VerifyEffectInstanceEdit(document with { Layers = layers }, document, cancellationToken);
    }

    private static int FindEffectInstance(ProjectLayer layer, Guid instanceId)
    {
        for (var index = 0; index < layer.Effects.Length; index++)
        {
            if (layer.Effects[index].Id == instanceId)
            {
                return index;
            }
        }
        throw new KeyNotFoundException("特效实例不存在。");
    }

    private static ProjectDocument VerifyEffectInstanceEdit(ProjectDocument document, ProjectDocument previous,
        CancellationToken cancellationToken)
    {
        ProjectValidator.Validate(document);
        ValidateEffectInstanceCompilation(document, previous, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }
}
