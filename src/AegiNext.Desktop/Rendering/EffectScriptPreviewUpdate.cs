using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectScriptPreviewUpdate(EffectScriptPreviewSnapshot Snapshot, SdrVideoFrame? Frame = null,
    bool ClearFrame = false, long Sequence = 0, bool CompletesCycle = false);
