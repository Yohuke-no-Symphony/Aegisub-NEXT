using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>图片裁剪或形状轮廓手势的完整结果，释放时提交一次。</summary>
public sealed class CanvasGraphicEditEventArgs(Guid layerId, LayerImage? image, LayerShape? shape) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public LayerImage? Image { get; } = image;
    public LayerShape? Shape { get; } = shape;
}
