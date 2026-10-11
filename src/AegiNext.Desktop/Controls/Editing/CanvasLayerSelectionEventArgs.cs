namespace AegiNext.Desktop.Controls;

/// <summary>按实际场景命中的待选片段。</summary>
public sealed class CanvasLayerSelectionEventArgs(Guid layerId) : EventArgs
{
    public Guid LayerId { get; } = layerId;
}
