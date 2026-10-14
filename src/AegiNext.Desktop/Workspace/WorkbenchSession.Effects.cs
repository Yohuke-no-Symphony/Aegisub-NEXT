using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void ClearKeyframeSelection() => layerEditing.ClearKeyframeSelection();
    internal void RefreshKeyframeInspector() => layerEditing.RefreshKeyframeInspector();
    internal void AddKeyframe()
    {
        if (SceneEditing.Target.Property == AnimationProperty.PATH_PROGRESS)
        {
            AddEffectPropertyKeyframe(SceneEditing.Target);
            return;
        }
        layerEditing.AddKeyframe();
    }
    internal void DeleteKeyframe() => layerEditing.DeleteKeyframe();
    internal void CreateKaraoke() => layerEditing.CreateKaraoke(null);
    internal void CreateKaraoke(Guid? presetId) => layerEditing.CreateKaraoke(presetId);
    internal void ResetPositionEffects() => layerEditing.ResetPositionEffects();
    internal void ResetAutomaticPosition() => layerEditing.ResetAutomaticPosition();
    internal void AddPathPoint() => layerEditing.AddPathPoint();
    internal void RemovePathPoint() => layerEditing.RemovePathPoint();
    internal void EditPath() => layerEditing.EditPath();
    internal void ClearPath() => layerEditing.ClearPath();
    internal void ClearKaraoke() => layerEditing.ClearKaraoke();
    internal Task ImportFontAsync() => layerEditing.ImportFontAsync();
    internal bool SelectKeyframe(TimelineKeyframeEventArgs value) => layerEditing.SelectKeyframe(value);
    internal void MoveKeyframe(TimelineKeyframeEventArgs value) => layerEditing.MoveKeyframe(value);
}
