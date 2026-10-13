using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewCacheEntry(EffectPresetPreviewCacheKey Key, SdrVideoFrame Frame, long Bytes);
