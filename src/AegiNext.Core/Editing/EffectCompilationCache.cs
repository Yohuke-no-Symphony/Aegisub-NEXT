using System.Text.Json;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>由场景准备服务拥有的有界执行计划缓存；上游手工动画值不参与编译身份。</summary>
public sealed class EffectCompilationCache
{
    private const int MAXIMUM_ENTRIES = 256;
    private readonly Lock gate = new();
    private readonly Dictionary<EffectPlanCacheKey, CompiledEffectScript> plans = [];
    private readonly Queue<EffectPlanCacheKey> order = [];
    private readonly Dictionary<string, EffectScript> scripts = new(StringComparer.Ordinal);

    public long CompilationCount { get; private set; }

    internal CompiledEffectScript GetOrCompile(EffectInstance instance, ProjectLayer layer, SubtitleLine? subtitle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var range = instance.TargetContext?.TextRangeId is { } id
            ? subtitle?.AnimationRanges.FirstOrDefault(value => value.Id == id) : null;
        var topology = layer.Mask is VectorClipMask mask
            ? string.Join(';', mask.Contours.Select(contour => contour.Id.ToString("D") + ":" +
                string.Join(',', contour.Nodes.Select(node => node.Id))))
            : layer.Mask?.GetType().Name ?? string.Empty;
        var key = new EffectPlanCacheKey(layer.Id, instance.Id, instance.Source,
            JsonSerializer.Serialize(instance.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.ToSource())).ToArray()),
            instance.Start, instance.End, layer.End - layer.Start, layer.AnimationOffset, layer.Kind,
            subtitle?.Id, subtitle?.Text ?? string.Empty, instance.TargetContext,
            range?.Utf16Start ?? 0, range?.Utf16Length ?? 0, topology);
        lock (gate)
        {
            if (plans.TryGetValue(key, out var existing))
            {
                return existing;
            }
            if (!scripts.TryGetValue(instance.Source, out var script))
            {
                script = EffectScriptParser.Parse(instance.Source);
                if (scripts.Count >= MAXIMUM_ENTRIES)
                {
                    scripts.Clear();
                }
                scripts.Add(instance.Source, script);
            }
            var plan = EffectScriptCompiler.CompileInstance(script, instance, layer, subtitle, cancellationToken);
            if (plans.Count >= MAXIMUM_ENTRIES)
            {
                plans.Remove(order.Dequeue());
            }
            plans.Add(key, plan);
            order.Enqueue(key);
            CompilationCount++;
            return plan;
        }
    }
}
