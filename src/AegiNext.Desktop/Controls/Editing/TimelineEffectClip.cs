using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineEffectClip(TimelineEffectClipId Id, bool IsCollapsed, double Top, double Height,
    string? RangeLabel = null, int ContourNumber = 0, int NodeNumber = 0)
{
    internal string Title => (Id.Target.NodeId.HasValue
        ? Localization.Format("Workbench.MaskNodeAnimationRow", AnimationPropertyLocalization.Get(Id.Target.Property),
            ContourNumber, NodeNumber)
        : AnimationPropertyLocalization.Get(Id.Target.Property)) +
        (Id.Target.TextRangeId.HasValue ? " · " + Localization.Get("Workbench.TextRangeAnimation") + " " + RangeLabel : string.Empty) +
        (Id.Target.State != SubtitleAnimationState.NORMAL ? " · " + Localization.Get(Id.Target.State == SubtitleAnimationState.ACTIVE
            ? "Workbench.ActiveAppearance" : "Workbench.InactiveAppearance") : string.Empty);
}
