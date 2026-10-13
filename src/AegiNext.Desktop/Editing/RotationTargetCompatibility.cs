using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

internal static class RotationTargetCompatibility
{
    internal static bool IsRotation(AnimationProperty property)
    {
        return property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.ROTATION_3D;
    }

    internal static AnimationTrackTarget Canonicalize(AnimationTrackTarget target)
    {
        return IsRotation(target.Property) ? target with { Property = AnimationProperty.ROTATION_3D } : target;
    }

    internal static int Component(AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.ROTATION_X => 0,
            AnimationProperty.ROTATION_Y => 1,
            AnimationProperty.ROTATION => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }
}
