using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>在一次可撤销事务中平移全部选中片段；冲突或非法成员不发布部分修改。</summary>
    public void ShiftClips(IReadOnlyCollection<Guid> layerIds, MediaTime offset)
    {
        Apply("Move clips", document => ProjectEditingOperations.ShiftClips(document, layerIds, offset));
    }

    /// <summary>在一次可撤销事务中同时移动两个相邻片段的共用交界。</summary>
    public void RollClipBoundary(Guid leftId, Guid rightId, MediaTime boundary)
    {
        Apply("Edit clips", document => ProjectEditingOperations.RollClipBoundary(document, leftId, rightId, boundary));
    }

    /// <summary>将捕获的同工程片段粘贴为独立副本，可将源轨道基准对齐目标轨道，一次事务返回副本身份。</summary>
    public ClipPasteResult PasteClips(ClipClipboardContent content, MediaTime start, Guid? targetTrackId = null)
    {
        ClipPasteResult? result = null;
        Apply("Paste clips", document =>
        {
            result = ProjectEditingOperations.PasteClips(document, content, start, targetTrackId);
            return result.Document;
        });
        return result! with { Document = Snapshot };
    }

    /// <summary>在一次可撤销事务中删除全部选中片段和相应字幕行，保留工程资源。</summary>
    public void RemoveClips(IReadOnlyCollection<Guid> layerIds)
    {
        Apply("Delete clips", document => ProjectEditingOperations.RemoveClips(document, layerIds));
    }
}
