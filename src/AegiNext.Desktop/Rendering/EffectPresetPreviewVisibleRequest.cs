namespace AegiNext.Desktop.Rendering;

internal sealed class EffectPresetPreviewVisibleRequest(EffectPresetPreviewRequest request, long generation)
{
    internal EffectPresetPreviewRequest Request { get; } = request;
    internal long Generation { get; } = generation;
    internal bool StaticQueued { get; set; }
    internal bool StaticInFlight { get; set; }
    internal bool StaticCompleted { get; set; }
}
