using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

internal sealed record LayerPositionPresetEdit(ScenePoint Anchor, bool SetPivot, bool ResetOffset) : LayerPositionDraftEdit
{
    internal override void ApplyTo(LayerPositionDraft target)
    {
        if (!target.SelectPreset(Anchor, SetPivot, ResetOffset))
        {
            throw new InvalidDataException("位置预设不能应用到目标草稿。");
        }
    }
}
