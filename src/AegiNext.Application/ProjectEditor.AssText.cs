using AegiNext.SubtitleFormats;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一次可撤销事务提交高级 ASS 编辑的文字、蒙版及动画。</summary>
    public void ApplyAssTextEdit(Guid subtitleId, AssTextEditResult edited)
    {
        Apply("Edit ASS subtitle", document => ProjectEditingOperations.ApplyAssTextEdit(document, subtitleId, edited));
    }
}
