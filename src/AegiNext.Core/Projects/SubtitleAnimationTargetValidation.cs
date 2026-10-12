namespace AegiNext.Core.Projects;

/// <summary>动画目标的身份及字幕、文本范围和蒙版归属校验，供精确编辑和工程边界共用。</summary>
public static class SubtitleAnimationTargetValidation
{
    /// <summary>验证不依赖实体存在性的完整目标身份。</summary>
    public static void ValidateIdentity(AnimationTrackTarget target)
    {
        if (!Enum.IsDefined(target.Property) || AnimationPropertyMetadata.IsLegacyComponent(target.Property) ||
            !Enum.IsDefined(target.State) || target.NodeId == Guid.Empty || target.TextRangeId == Guid.Empty ||
            target.NodeId.HasValue && target.TextRangeId.HasValue ||
            AnimationPropertyMetadata.IsNodeProperty(target.Property) != target.NodeId.HasValue ||
            target.TextRangeId.HasValue && !AnimationPropertyMetadata.IsTextRangeProperty(target.Property) ||
            target.State != SubtitleAnimationState.NORMAL && !AnimationPropertyMetadata.IsSubtitleVisualProperty(target.Property))
        {
            throw new InvalidDataException("The animation target property, node, text range, or drawing state is invalid.");
        }
    }

    /// <summary>验证真实编辑目标引用所属字幕范围或蒙版；仅显式未绑定模式允许尚未解析的范围引用。</summary>
    public static void Validate(AnimationTrackTarget target, SubtitleLine? subtitle = null, ClipMask? mask = null,
        bool allowUnboundTextRange = false)
    {
        ValidateIdentity(target);
        if ((target.TextRangeId.HasValue || target.State != SubtitleAnimationState.NORMAL ||
            AnimationPropertyMetadata.IsSubtitleOnlyProperty(target.Property)) && subtitle is null && !allowUnboundTextRange)
        {
            throw new InvalidDataException("The subtitle animation target is missing its owning subtitle.");
        }
        if (target.TextRangeId is { } rangeId && !allowUnboundTextRange &&
            !subtitle!.AnimationRanges.Any(range => range.Id == rangeId))
        {
            throw new InvalidDataException("The text animation range does not belong to the current subtitle.");
        }
        if (!AnimationPropertyMetadata.IsMaskProperty(target.Property))
        {
            return;
        }
        if (mask is null || target.Property is AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT &&
            mask is not RectangleClipMask || target.NodeId is { } nodeId &&
            (mask is not VectorClipMask vector || !vector.Contours.Any(contour => contour.Nodes.Any(node => node.Id == nodeId))))
        {
            throw new InvalidDataException("The mask animation target is missing its geometry or node.");
        }
    }
}
