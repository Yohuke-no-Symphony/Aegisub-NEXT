using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>蒙版轨道的基础值、拓扑身份与不可变几何编辑，供工作台、脚本和求值器共享。</summary>
public static class ClipMaskAnimation
{
    /// <summary>判断片段是否存在节点形变轨道，存在时轮廓和节点连接关系保持固定。</summary>
    public static bool IsTopologyLocked(ProjectLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return layer.Tracks.Any(track => AnimationPropertyMetadata.IsNodeProperty(track.Property));
    }

    /// <summary>比较类型、轮廓及节点顺序和身份；几何数值不参与拓扑比较。</summary>
    public static bool HasSameTopology(ClipMask? first, ClipMask? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is RectangleClipMask && second is RectangleClipMask)
        {
            return true;
        }

        if (first is not VectorClipMask a || second is not VectorClipMask b || a.Contours.Length != b.Contours.Length)
        {
            return false;
        }

        for (var index = 0; index < a.Contours.Length; index++)
        {
            if (a.Contours[index].Id != b.Contours[index].Id ||
                !a.Contours[index].Nodes.Select(node => node.Id).SequenceEqual(b.Contours[index].Nodes.Select(node => node.Id)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>从静态几何读取完整目标的基础值；不存在的形状或节点目标明确失败。</summary>
    public static AnimationValue GetBaseValue(ClipMask mask, AnimationTrackTarget target)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ValidateIdentity(target);
        return target.Property switch
        {
            AnimationProperty.MASK_RECTANGLE_TOP_LEFT when mask is RectangleClipMask rectangle => rectangle.TopLeft,
            AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT when mask is RectangleClipMask rectangle => rectangle.BottomRight,
            AnimationProperty.MASK_POSITION => mask.Transform.Position,
            AnimationProperty.MASK_SCALE => mask.Transform.Scale,
            AnimationProperty.MASK_ROTATION => mask.Transform.Rotation,
            AnimationProperty.MASK_NODE_POSITION => FindNode(mask, target).Position,
            AnimationProperty.MASK_NODE_IN_HANDLE => FindNode(mask, target).InHandle,
            AnimationProperty.MASK_NODE_OUT_HANDLE => FindNode(mask, target).OutHandle,
            _ => throw new InvalidDataException("蒙版形状与动画目标不匹配。")
        };
    }

    /// <summary>替换完整目标的基础值并保留所有稳定身份、原生定位及固定参考框。</summary>
    public static ClipMask SetBaseValue(ClipMask mask, AnimationTrackTarget target, AnimationValue value)
    {
        var current = GetBaseValue(mask, target);
        if (current.Kind != value.Kind)
        {
            throw new InvalidDataException("蒙版动画目标与值维度不匹配。");
        }

        if (current == value)
        {
            return mask;
        }

        return target.Property switch
        {
            AnimationProperty.MASK_RECTANGLE_TOP_LEFT => ((RectangleClipMask)mask) with { TopLeft = value.Vector },
            AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT => ((RectangleClipMask)mask) with { BottomRight = value.Vector },
            AnimationProperty.MASK_POSITION => mask with { Transform = mask.Transform with { Position = value.Vector } },
            AnimationProperty.MASK_SCALE => mask with { Transform = mask.Transform with { Scale = value.Vector } },
            AnimationProperty.MASK_ROTATION => mask with { Transform = mask.Transform with { Rotation = value.Scalar } },
            _ => EditNode((VectorClipMask)mask, target, value.Vector)
        };
    }

    private static void ValidateIdentity(AnimationTrackTarget target)
    {
        if (!AnimationPropertyMetadata.IsMaskProperty(target.Property) ||
            AnimationPropertyMetadata.IsNodeProperty(target.Property) != target.NodeId.HasValue || target.NodeId == Guid.Empty)
        {
            throw new InvalidDataException("蒙版动画目标身份无效。");
        }
    }

    private static MaskNode FindNode(ClipMask mask, AnimationTrackTarget target)
    {
        if (mask is VectorClipMask vector)
        {
            foreach (var contour in vector.Contours)
            {
                foreach (var node in contour.Nodes)
                {
                    if (node.Id == target.NodeId)
                    {
                        return node;
                    }
                }
            }
        }

        throw new InvalidDataException($"蒙版动画目标节点不存在：{target.NodeId}。");
    }

    private static VectorClipMask EditNode(VectorClipMask mask, AnimationTrackTarget target, ScenePoint value)
    {
        return mask with
        {
            Contours = mask.Contours.Select(contour => contour.Nodes.Any(node => node.Id == target.NodeId) ? contour with
            {
                Nodes = contour.Nodes.Select(node => node.Id != target.NodeId ? node : target.Property switch
                {
                    AnimationProperty.MASK_NODE_POSITION => node with { Position = value },
                    AnimationProperty.MASK_NODE_IN_HANDLE => node with { InHandle = value },
                    AnimationProperty.MASK_NODE_OUT_HANDLE => node with { OutHandle = value },
                    _ => throw new InvalidDataException("未知蒙版节点属性。")
                }).ToImmutableArray()
            } : contour).ToImmutableArray()
        };
    }
}
