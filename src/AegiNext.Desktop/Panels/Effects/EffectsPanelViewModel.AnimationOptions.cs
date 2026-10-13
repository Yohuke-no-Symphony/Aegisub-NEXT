using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    public string AnimationOptionsLabel => AnimationPropertyLocalization.Get(Property);
    public bool NeedsAnimationKeyframe => !IsOrderedTransform && !CanEditKeyframes;
    public bool CanEditColorSpace => IsColorProperty && (IsOrderedTransform || CanEditKeyframes);

    internal void RefreshAnimationOptionsState()
    {
        OnPropertyChanged(nameof(AnimationOptionsLabel));
        OnPropertyChanged(nameof(CanEditKeyframes));
        OnPropertyChanged(nameof(NeedsAnimationKeyframe));
        OnPropertyChanged(nameof(CanEditColorSpace));
    }
}
