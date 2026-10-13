using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Editing;

internal static class AnimationPropertyLocalization
{
    internal static string Get(AnimationProperty value)
    {
        var key = value switch
        {
            AnimationProperty.POSITION => "PositionVector", AnimationProperty.SCALE => "ScaleVector",
            AnimationProperty.POSITION_X => "PositionX", AnimationProperty.POSITION_Y => "PositionY",
            AnimationProperty.SCALE_X => "ScaleX", AnimationProperty.SCALE_Y => "ScaleY", AnimationProperty.ROTATION => "RotationZ",
            AnimationProperty.ROTATION_X => "RotationX", AnimationProperty.ROTATION_Y => "RotationY",
            AnimationProperty.PERSPECTIVE_DISTANCE => "PerspectiveDistance",
            AnimationProperty.FILL => "Fill", AnimationProperty.STROKE => "Stroke",
            AnimationProperty.OPACITY => "Opacity", AnimationProperty.FILL_RED => "FillRed", AnimationProperty.FILL_GREEN => "FillGreen",
            AnimationProperty.FILL_BLUE => "FillBlue", AnimationProperty.FILL_ALPHA => "FillAlpha", AnimationProperty.STROKE_RED => "StrokeRed",
            AnimationProperty.STROKE_GREEN => "StrokeGreen", AnimationProperty.STROKE_BLUE => "StrokeBlue", AnimationProperty.STROKE_ALPHA => "StrokeAlpha",
            AnimationProperty.STROKE_WIDTH => "StrokeWidth", AnimationProperty.BLUR => "Blur", AnimationProperty.PATH_PROGRESS => "PathProgress",
            AnimationProperty.LETTER_SPACING => "LetterSpacing", AnimationProperty.FILL_BLUR => "FillBlur",
            AnimationProperty.STROKE_BLUR => "StrokeBlur",
            AnimationProperty.FONT_SIZE => "Size", AnimationProperty.SHADOW_OFFSET => "ShadowOffset",
            AnimationProperty.SHADOW_BLUR => "ShadowBlur", AnimationProperty.SHADOW_COLOR => "ShadowColor",
            _ when AnimationPropertyMetadata.IsMaskProperty(value) => value.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        return Localization.Get("Workbench." + key);
    }
}
