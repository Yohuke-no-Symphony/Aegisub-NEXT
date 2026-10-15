using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一个撤销事务保存动画行布局及其联动的特效实例顺序。</summary>
    public void MoveTimelineEffectRow(Guid layerId, TimelineEffectRowIdentity id, TimelineEffectRowIdentity? beforeId)
    {
        Apply("Reorder animation row", document => ProjectEditingOperations.MoveTimelineEffectRow(document, layerId, id, beforeId));
    }
}
