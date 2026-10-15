using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>在一个可撤销事务中为片段添加完整时段的源码快照特效。</summary>
    public void AddEffectInstances(Guid layerId, string name, string source,
        ImmutableDictionary<string, EffectScriptParameterValue>? parameters = null, AnimationTrackTarget? targetContext = null)
    {
        AddEffectInstances([layerId], name, source, parameters, targetContext);
    }

    /// <summary>为选中片段批量添加有序实例，任何成员失败都不修改快照和历史。</summary>
    public void AddEffectInstances(IReadOnlyCollection<Guid> layerIds, string name, string source,
        ImmutableDictionary<string, EffectScriptParameterValue>? parameters = null, AnimationTrackTarget? targetContext = null)
    {
        Apply("Add effect instances", document => ProjectEditingOperations.AddEffectInstances(document, layerIds,
            name, source, parameters, targetContext));
    }

    /// <summary>一次提交实例的全部固定参数；非法参数保留原实例及历史。</summary>
    public void UpdateEffectInstance(Guid layerId, Guid instanceId, ImmutableDictionary<string, EffectScriptParameterValue> parameters)
    {
        Apply("Update effect parameters", document => ProjectEditingOperations.UpdateEffectInstance(document, layerId, instanceId, parameters));
    }

    /// <summary>原子更新一组明确实例，保留各自稳定身份。</summary>
    public void UpdateEffectInstances(IReadOnlyCollection<EffectInstanceId> instances, Func<EffectInstance, EffectInstance> update)
    {
        Apply("Update effect instances", document => ProjectEditingOperations.UpdateEffectInstances(document, instances, update));
    }

    /// <summary>设置实例内容时段并重新校验时长适配，对应一次撤销。</summary>
    public void SetEffectInstanceTiming(Guid layerId, Guid instanceId, MediaTime start, MediaTime end)
    {
        Apply("Set effect instance timing", document => ProjectEditingOperations.SetEffectInstanceTiming(document, layerId, instanceId, start, end));
    }

    /// <summary>启用或禁用一个实例，不修改基础动画。</summary>
    public void SetEffectInstanceEnabled(Guid layerId, Guid instanceId, bool isEnabled)
    {
        SetEffectInstanceEnabled([new(layerId, instanceId)], isEnabled);
    }

    /// <summary>一次启用或禁用多个实例，保留栈顺序。</summary>
    public void SetEffectInstanceEnabled(IReadOnlyCollection<EffectInstanceId> instances, bool isEnabled)
    {
        Apply("Set effect instance enabled", document => ProjectEditingOperations.SetEffectInstanceEnabled(document, instances, isEnabled));
    }

    /// <summary>将实例移动到同一父片段的指定零基栈位置。</summary>
    public void MoveEffectInstance(Guid layerId, Guid instanceId, int newIndex)
    {
        Apply("Reorder effect instance", document => ProjectEditingOperations.MoveEffectInstance(document, layerId, instanceId, newIndex));
    }

    /// <summary>只删除选中特效实例，保留父片段。</summary>
    public void RemoveEffectInstance(Guid layerId, Guid instanceId)
    {
        RemoveEffectInstances([new(layerId, instanceId)]);
    }

    /// <summary>以一个撤销事务删除全部指定实例。</summary>
    public void RemoveEffectInstances(IReadOnlyCollection<EffectInstanceId> instances)
    {
        Apply("Remove effect instances", document => ProjectEditingOperations.RemoveEffectInstances(document, instances));
    }
}
