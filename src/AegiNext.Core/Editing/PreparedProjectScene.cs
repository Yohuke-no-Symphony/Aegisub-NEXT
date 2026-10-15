using System.Collections.Frozen;
using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>只验证一次的不可变渲染快照；重复帧求值复用字幕索引而不重复验证全文。</summary>
public sealed class PreparedProjectScene
{
    /// <summary>验证工程并准备只读索引；工程快照变化后创建新实例。</summary>
    public PreparedProjectScene(ProjectDocument document, EffectCompilationCache? effectCache = null)
    {
        ProjectValidator.Validate(document);
        Document = document;
        Subtitles = document.Subtitles.ToFrozenDictionary(line => line.Id);
        Clips = new(document);
        Layers = document.Layers.ToFrozenDictionary(layer => layer.Id);
        effectCache ??= new();
        Effects = document.Layers.Where(layer => !layer.Effects.IsEmpty).ToFrozenDictionary(layer => layer.Id,
            layer => PreparedEffectLayer.Create(layer, layer.SubtitleId is { } id ? Subtitles[id] : null, effectCache));
    }

    public ProjectDocument Document { get; }
    public ProjectClipIndex Clips { get; }

    /// <summary>读取指定片段的只读特效计划，供外部字幕格式构造临时导出投影。</summary>
    public ImmutableArray<CompiledEffectScript> GetEffectPlans(Guid layerId)
    {
        return Effects.TryGetValue(layerId, out var layer)
            ? layer.Instances.Select(instance => instance.Plan).ToImmutableArray() : [];
    }
    internal FrozenDictionary<Guid, SubtitleLine> Subtitles { get; }
    internal FrozenDictionary<Guid, PreparedEffectLayer> Effects { get; }
    internal FrozenDictionary<Guid, ProjectLayer> Layers { get; }
}
