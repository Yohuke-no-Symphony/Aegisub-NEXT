using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewWork(EffectPresetPreviewRequest Request, long Generation,
    MediaTime Position, bool IsAnimated, CancellationTokenSource Cancellation);
