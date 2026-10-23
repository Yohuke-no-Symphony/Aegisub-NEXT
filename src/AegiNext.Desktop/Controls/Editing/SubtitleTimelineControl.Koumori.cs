using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Koumori;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private IReadOnlyList<KoumoriBoundary> frozenKoumoriBoundaries = [];
    public bool IsKoumoriAvailable { get; set; }
    public bool IsKoumoriSnapEnabled { get; set; }
    public int KoumoriMaximumSnapMilliseconds { get; set; } = 250;
    public IReadOnlyList<KoumoriBoundary> KoumoriBoundaries { get; set; } = [];

    private void FreezeKoumoriBoundaries(Guid id)
    {
        frozenKoumoriBoundaries = layersById.TryGetValue(id, out var layer) && layer.Kind == LayerKind.SUBTITLE ||
            cuesById.ContainsKey(id) ? KoumoriBoundaries.ToArray() : [];
    }

    private bool ShouldSnapKoumori(KeyModifiers modifiers, bool classic = false) => IsKoumoriAvailable &&
        (IsKoumoriSnapEnabled != (classic && (modifiers & KeyModifiers.Shift) != 0)) &&
        (modifiers & KeyModifiers.Alt) == 0;

    private MediaTime SnapSubtitleEdge(MediaTime value, KeyModifiers modifiers, KoumoriBoundaryKind kind)
    {
        var quantized = QuantizeEdit(value, modifiers);
        var ordinary = ShouldSnap(modifiers)
            ? TimelineQuantization.ResolveSnap(quantized, snapBoundaries, PixelsPerSecond, position)
            : new TimelineSnapResult(quantized, null);
        var snap = ShouldSnapKoumori(modifiers)
            ? KoumoriSnapResolver.Resolve(quantized, kind, frozenKoumoriBoundaries, PixelsPerSecond,
                KoumoriMaximumSnapMilliseconds, ordinary) : ordinary;
        snapTarget = snap.Boundary;
        return snap.Value;
    }

    private TimelineSnapResult SnapSubtitleMove(MediaTime start, MediaTime end, KeyModifiers modifiers)
    {
        var ordinary = ShouldSnap(modifiers)
            ? TimelineQuantization.ResolveSnapOffset(start, end, snapBoundaries, PixelsPerSecond, position)
            : new TimelineSnapResult(MediaTime.Zero, null);
        if (!ShouldSnapKoumori(modifiers))
        {
            return ordinary;
        }
        var startSnap = KoumoriSnapResolver.Resolve(start, KoumoriBoundaryKind.Start, frozenKoumoriBoundaries,
            PixelsPerSecond, KoumoriMaximumSnapMilliseconds, new(start + ordinary.Value, ordinary.Boundary));
        var endSnap = KoumoriSnapResolver.Resolve(end, KoumoriBoundaryKind.End, frozenKoumoriBoundaries,
            PixelsPerSecond, KoumoriMaximumSnapMilliseconds, new(end + ordinary.Value, ordinary.Boundary));
        var startDistance = startSnap.Boundary.HasValue ? Math.Abs(Seconds(startSnap.Value - start)) : double.PositiveInfinity;
        var endDistance = endSnap.Boundary.HasValue ? Math.Abs(Seconds(endSnap.Value - end)) : double.PositiveInfinity;
        return startDistance <= endDistance
            ? new(startSnap.Value - start, startSnap.Boundary)
            : new(endSnap.Value - end, endSnap.Boundary);
    }
}
