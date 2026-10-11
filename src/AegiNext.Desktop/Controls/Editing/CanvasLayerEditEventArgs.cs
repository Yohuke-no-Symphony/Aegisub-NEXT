using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>画布拖拽完成后的局部层数据；一次拖拽提交一次事务。</summary>
public sealed class CanvasLayerEditEventArgs : EventArgs
{
    /// <summary>创建层变换、路径和可选形状尺寸编辑结果。</summary>
    public CanvasLayerEditEventArgs(Guid layerId, LayerTransform transform, MotionPath? path, LayerShape? shape = null)
    {
        LayerId = layerId;
        Transform = transform;
        Path = path;
        Shape = shape;
    }

    public Guid LayerId { get; }
    public LayerTransform Transform { get; }
    public MotionPath? Path { get; }
    public LayerShape? Shape { get; }
}
