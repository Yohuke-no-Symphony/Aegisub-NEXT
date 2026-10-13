using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewResult(EffectPresetPreviewRequest Request, long Generation,
    MediaTime Position, bool IsAnimated, SdrVideoFrame? Frame, Exception? Error);
