using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>按视觉从上到下移动一个完整动画行；空锚点表示末尾，实例相对顺序同步到求值栈。</summary>
    public static ProjectDocument MoveTimelineEffectRow(ProjectDocument document, Guid layerId,
        TimelineEffectRowIdentity id, TimelineEffectRowIdentity? beforeId)
    {
        ArgumentNullException.ThrowIfNull(id);
        id.Validate();
        beforeId?.Validate();
        ProjectValidator.Validate(document);
        var layer = document.Layers.FirstOrDefault(value => value.Id == layerId) ?? throw new KeyNotFoundException("片段不存在。");
        var subtitle = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        var rows = TimelineEffectRowOrdering.GetOrder(layer, subtitle);
        var source = rows.IndexOf(id);
        var before = beforeId is null ? rows.Length : rows.IndexOf(beforeId);
        if (source < 0 || before < 0)
        {
            throw new KeyNotFoundException("动画行或插入锚点不存在于指定片段。");
        }
        var destination = before > source ? before - 1 : before;
        if (destination == source)
        {
            return document;
        }
        var order = rows.RemoveAt(source).Insert(destination, id);
        var effects = layer.Effects.ToDictionary(instance => instance.Id);
        var reordered = order.Where(identity => identity.InstanceId.HasValue).Reverse()
            .Select(identity => effects[identity.InstanceId!.Value]).ToImmutableArray();
        var next = layer with
        {
            EffectRowOrder = order,
            Effects = reordered.SequenceEqual(layer.Effects) ? layer.Effects : reordered
        };
        return VerifyEffectInstanceEdit(document with { Layers = document.Layers.Replace(layer, next) }, document, default);
    }
}
