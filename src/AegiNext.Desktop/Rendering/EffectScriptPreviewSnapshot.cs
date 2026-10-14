using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectScriptPreviewSnapshot(EffectScriptPreviewRequest? Request, long Generation,
    MediaTime Position, bool IsPlaying, bool IsBusy, bool IsScrubbing, bool IsVisible, bool Loop, Exception? Error);
