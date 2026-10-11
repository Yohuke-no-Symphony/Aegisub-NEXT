using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineRow(Guid TrackId, string Name, IReadOnlyList<ProjectLayer> Clips,
    bool IsCollapsed, double Top, double Height, TimelineEffectClipLayout Effects,
    string? StylePresetName = null, bool AutoApplyStyle = true)
{
    private const double STYLE_BADGE_GAP = 2;
    internal double HeaderContentHeight => Height - CurveHeight;
    internal double CurveHeight => Effects.Height;
    internal Rect? StyleBadgeRectangle(double rowY, double headerWidth) => StylePresetName is null
        ? null : new Rect(26, ExpanderRectangle(rowY).Bottom + STYLE_BADGE_GAP,
            Math.Max(0, headerWidth - 34), 18);
    internal static Rect ExpanderRectangle(double rowY) => new(4, rowY + 4, 20, 20);
}
